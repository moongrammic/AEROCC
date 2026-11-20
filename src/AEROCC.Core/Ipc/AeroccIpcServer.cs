using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AEROCC.Core.Display;
using AEROCC.Core.Models;
using AEROCC.Core.Wmi;

namespace AEROCC.Core.Ipc;

public class AeroccIpcServer : IDisposable
{
    private const int MaxRequestLength = 32 * 1024; // 32 KB limit per IPC command
    private const int MaxConcurrentClients = 16;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _concurrencySemaphore = new(MaxConcurrentClients, MaxConcurrentClients);
    private readonly GigabyteWmiClient _wmiClient;
    private readonly Func<HardwareStatus> _getStatusFunc;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public AeroccIpcServer(GigabyteWmiClient wmiClient, Func<HardwareStatus> getStatusFunc)
    {
        _wmiClient = wmiClient;
        _getStatusFunc = getStatusFunc;
    }

    public void Start()
    {
        if (_isRunning) return;

        _isRunning = true;
        _cts = new CancellationTokenSource();
        _ = ListenLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                var pipeSecurity = new PipeSecurity();
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));

                serverStream = NamedPipeServerStreamAcl.Create(
                    IpcConstants.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096, 4096,
                    pipeSecurity);

                await serverStream.WaitForConnectionAsync(ct);

                // Handle client in background task with bounded concurrency, transferring ownership of stream
                var clientStream = serverStream;
                serverStream = null;

                try
                {
                    _ = Task.Run(async () =>
                    {
                        using (clientStream)
                        {
                            try
                            {
                                await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);
                                try
                                {
                                    await HandleClientAsync(clientStream, ct).ConfigureAwait(false);
                                }
                                finally
                                {
                                    _concurrencySemaphore.Release();
                                }
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[IPC Task Error]: {ex.Message}");
                            }
                        }
                    });
                }
                catch
                {
                    clientStream.Dispose();
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (UnauthorizedAccessException uex)
            {
                // Another process or service already owns the pipe
                Console.WriteLine($"[IPC Server Loop Warning]: Pipe is already hosted by another running instance of AEROCC.Service: {uex.Message}");
                await Task.Delay(2000, ct);
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Console.WriteLine($"[IPC Server Loop Error]: {ex.Message}");
                    await Task.Delay(500, ct);
                }
            }
            finally
            {
                serverStream?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeout);
            var clientToken = timeoutCts.Token;

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

            var (line, exceeded) = await ReadBoundedLineAsync(reader, MaxRequestLength, clientToken);
            if (exceeded)
            {
                var errResp = new IpcResponse { Success = false, ErrorMessage = "Request payload exceeds maximum allowed size (32KB)." };
                await writer.WriteLineAsync(JsonSerializer.Serialize(errResp).AsMemory(), clientToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(line)) return;

            var request = JsonSerializer.Deserialize<IpcMessage>(line);
            var response = ProcessRequest(request, stream);

            var jsonResp = JsonSerializer.Serialize(response);
            await writer.WriteLineAsync(jsonResp.AsMemory(), clientToken);
            await writer.FlushAsync(clientToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on timeout or service shutdown
        }
        catch (IOException)
        {
            // Expected when client disconnects or closes
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IPC Client Handler Error]: {ex.Message}");
        }
    }

    private static async Task<(string? Line, bool Exceeded)> ReadBoundedLineAsync(StreamReader reader, int maxChars, CancellationToken ct)
    {
        var sb = new StringBuilder();
        char[] buffer = new char[512];

        while (!ct.IsCancellationRequested)
        {
            int toRead = Math.Min(buffer.Length, maxChars - sb.Length + 1);
            if (toRead <= 0)
            {
                return (null, true);
            }

            int read = await reader.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0)
            {
                break;
            }

            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (c == '\n')
                {
                    return (sb.ToString().TrimEnd('\r'), false);
                }
                sb.Append(c);
                if (sb.Length > maxChars)
                {
                    return (null, true);
                }
            }
        }

        return (sb.Length > 0 ? sb.ToString().TrimEnd('\r') : null, false);
    }

    private IpcResponse ProcessRequest(IpcMessage? request, NamedPipeServerStream stream)
    {
        if (request == null)
            return new IpcResponse { Success = false, ErrorMessage = "Invalid request format" };

        bool isReadOnly = request.Command is IpcConstants.CmdGetStatus or IpcConstants.CmdPing;
        if (!isReadOnly && !IsClientAuthorized(stream))
        {
            return new IpcResponse
            {
                Success = false,
                ErrorMessage = "Access denied: Administrative privileges required to change hardware settings."
            };
        }

        try
        {
            switch (request.Command)
            {
                case IpcConstants.CmdGetStatus:
                    return new IpcResponse { Success = true, Status = _getStatusFunc() };

                case IpcConstants.CmdSetBatteryLimit:
                    if (int.TryParse(request.Payload, out int limit))
                    {
                        bool ok = _wmiClient.SetBatteryChargeLimit(limit);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid battery limit value" };

                case IpcConstants.CmdSetFanProfile:
                    if (int.TryParse(request.Payload, out int prof) && Enum.IsDefined(typeof(FanProfile), prof))
                    {
                        bool ok = _wmiClient.SetFanProfile((FanProfile)prof);
                        return new IpcResponse
                        {
                            Success = ok,
                            ErrorMessage = ok ? null : "Не удалось записать профиль вентиляторов через WMI.",
                            Status = _getStatusFunc()
                        };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid fan profile" };

                case IpcConstants.CmdSetFixedFanSpeed:
                    if (byte.TryParse(request.Payload, out byte speed))
                    {
                        bool ok = _wmiClient.SetFixedFanSpeedFull(speed);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid fan speed value" };

                case IpcConstants.CmdSetFanCurvePoint:
                    if (!string.IsNullOrEmpty(request.Payload))
                    {
                        var pts = new List<(byte Temp, byte Duty)>();
                        foreach (var item in request.Payload.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = item.Split(',');
                            if (parts.Length == 2 && byte.TryParse(parts[0], out byte t) && byte.TryParse(parts[1], out byte d))
                            {
                                pts.Add((t, d));
                            }
                        }
                        if (pts.Count > 0)
                        {
                            bool ok = _wmiClient.ApplyFanCurve(pts);
                            return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                        }
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid FanCurve payload" };

                case IpcConstants.CmdSetWinLock:
                    if (bool.TryParse(request.Payload, out bool winLocked))
                    {
                        bool ok = _wmiClient.SetWinKeyLock(winLocked);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid WinLock value" };

                case IpcConstants.CmdSetFnLock:
                    if (bool.TryParse(request.Payload, out bool fnLocked))
                    {
                        bool ok = _wmiClient.SetFnKeyLock(fnLocked);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid FnLock value" };

                case IpcConstants.CmdSetRefreshRate:
                    if (int.TryParse(request.Payload, out int hz))
                    {
                        bool ok = DisplayManager.SetRefreshRate(hz);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid refresh rate value" };

                case IpcConstants.CmdSetBacklightBrightness:
                    if (byte.TryParse(request.Payload, out byte brightness))
                    {
                        bool ok = _wmiClient.Keyboard.SetBrightness(brightness);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid brightness value" };

                case IpcConstants.CmdSetBacklightColor:
                    if (byte.TryParse(request.Payload, out byte colorCode) && Enum.IsDefined(typeof(KeyboardColor), colorCode))
                    {
                        byte curBrightness = _wmiClient.Keyboard.LastBrightness > 0 ? _wmiClient.Keyboard.LastBrightness : (byte)100;
                        var targetColor = (KeyboardColor)colorCode;
                        KeyboardEffect eff = targetColor == KeyboardColor.Random ? KeyboardEffect.Wave : KeyboardEffect.Static;
                        bool ok = _wmiClient.Keyboard.SetBacklight(eff, curBrightness, targetColor);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid color value" };

                case IpcConstants.CmdSetBacklightEffect:
                    if (byte.TryParse(request.Payload, out byte effectCode) && Enum.IsDefined(typeof(KeyboardEffect), effectCode))
                    {
                        byte curBrightness = _wmiClient.Keyboard.LastBrightness > 0 ? _wmiClient.Keyboard.LastBrightness : (byte)100;
                        var eff = (KeyboardEffect)effectCode;
                        KeyboardColor clr = (eff == KeyboardEffect.Wave || eff == KeyboardEffect.Neon) ? KeyboardColor.Random : _wmiClient.Keyboard.LastColor;
                        bool ok = _wmiClient.Keyboard.SetBacklight(eff, curBrightness, clr);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid effect value" };

                case IpcConstants.CmdSetBacklightFull:
                    if (!string.IsNullOrEmpty(request.Payload))
                    {
                        var parts = request.Payload.Split(',');
                        if (parts.Length >= 5 &&
                            byte.TryParse(parts[0], out byte eff) &&
                            byte.TryParse(parts[1], out byte br) &&
                            byte.TryParse(parts[2], out byte clr) &&
                            byte.TryParse(parts[3], out byte spd) &&
                            byte.TryParse(parts[4], out byte dir))
                        {
                            bool ok = _wmiClient.Keyboard.SetBacklight(
                                (KeyboardEffect)eff,
                                br,
                                (KeyboardColor)clr,
                                spd,
                                dir);
                            return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                        }
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid BacklightFull payload format" };

                case IpcConstants.CmdSetCustomRgb:
                    if (!string.IsNullOrEmpty(request.Payload))
                    {
                        var parts = request.Payload.Split(',');
                        if (parts.Length >= 3 &&
                            byte.TryParse(parts[0], out byte r) &&
                            byte.TryParse(parts[1], out byte g) &&
                            byte.TryParse(parts[2], out byte b))
                        {
                            byte br = parts.Length > 3 && byte.TryParse(parts[3], out byte customBr) ? customBr : (byte)100;
                            bool ok = _wmiClient.Keyboard.SetCustomColor(r, g, b, br);
                            return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                        }
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid RGB payload format (expected 'R,G,B' or 'R,G,B,Brightness')" };

                case IpcConstants.CmdSetGpuPowerMode:
                    if (int.TryParse(request.Payload, out int gpuMode) && Enum.IsDefined(typeof(AEROCC.Core.Gpu.GpuPowerMode), gpuMode))
                    {
                        bool ok = _wmiClient.SetGpuPowerMode((AEROCC.Core.Gpu.GpuPowerMode)gpuMode);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid GPU power mode value" };

                case IpcConstants.CmdSetApuPower:
                    if (!string.IsNullOrEmpty(request.Payload))
                    {
                        var parts = request.Payload.Split(',');
                        if (parts.Length >= 3 &&
                            double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out double fPpt) &&
                            double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out double sPpt) &&
                            double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out double spl))
                        {
                            bool ok = _wmiClient.SetApuPower(fPpt, sPpt, spl);
                            return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                        }
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid ApuPower payload format (expected 'fPPT,sPPT,SPL')" };

                case IpcConstants.CmdPing:
                    return new IpcResponse { Success = true };

                default:
                    return new IpcResponse { Success = false, ErrorMessage = $"Unknown command: {request.Command}" };
            }
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static bool IsClientAuthorized(NamedPipeServerStream stream)
    {
        try
        {
            bool authorized = false;
            stream.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                // Authorize local interactive desktop user, administrators, or LocalSystem
                authorized = (identity.IsAuthenticated && !identity.IsAnonymous) || identity.IsSystem;
            });
            return authorized;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _concurrencySemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
