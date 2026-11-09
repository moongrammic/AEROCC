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
            try
            {
                var pipeSecurity = new PipeSecurity();
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));

                var serverStream = NamedPipeServerStreamAcl.Create(
                    IpcConstants.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0, 0,
                    pipeSecurity);

                await serverStream.WaitForConnectionAsync(ct);

                // Handle client in background task, transferring ownership of stream
                _ = Task.Run(() => HandleClientAsync(serverStream, ct), ct);
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
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        try
        {
            using (stream)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) return;

                var request = JsonSerializer.Deserialize<IpcMessage>(line);
                var response = ProcessRequest(request);

                var jsonResp = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(jsonResp.AsMemory(), ct);
            }
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

    private IpcResponse ProcessRequest(IpcMessage? request)
    {
        if (request == null)
            return new IpcResponse { Success = false, ErrorMessage = "Invalid request format" };

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
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid fan profile" };

                case IpcConstants.CmdSetFixedFanSpeed:
                    if (byte.TryParse(request.Payload, out byte speed))
                    {
                        bool ok = _wmiClient.SetFixedFanSpeed(speed);
                        return new IpcResponse { Success = ok };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid fan speed value" };

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
                        bool ok = _wmiClient.Keyboard.SetBacklight(KeyboardEffect.Static, 100, (KeyboardColor)colorCode);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid color value" };

                case IpcConstants.CmdSetBacklightEffect:
                    if (byte.TryParse(request.Payload, out byte effectCode) && Enum.IsDefined(typeof(KeyboardEffect), effectCode))
                    {
                        bool ok = _wmiClient.Keyboard.SetBacklight((KeyboardEffect)effectCode, 100);
                        return new IpcResponse { Success = ok, Status = _getStatusFunc() };
                    }
                    return new IpcResponse { Success = false, ErrorMessage = "Invalid effect value" };

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

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
