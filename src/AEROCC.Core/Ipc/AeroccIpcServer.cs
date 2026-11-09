using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AEROCC.Core.Models;
using AEROCC.Core.Wmi;

namespace AEROCC.Core.Ipc;

public class AeroccIpcServer
{
    private readonly GigabyteWmiClient _wmiClient;
    private readonly Func<HardwareStatus> _getStatusFunc;
    private CancellationTokenSource? _cts;

    public AeroccIpcServer(GigabyteWmiClient wmiClient, Func<HardwareStatus> getStatusFunc)
    {
        _wmiClient = wmiClient;
        _getStatusFunc = getStatusFunc;
    }

    public void Start(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ListenLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipeSecurity = new PipeSecurity();
                var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(authenticatedUsersSid, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));

                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

                var serverStream = NamedPipeServerStreamAcl.Create(
                    IpcConstants.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity
                );

                await serverStream.WaitForConnectionAsync(ct);
                // Ownership of serverStream is passed to HandleClientAsync which disposes it
                _ = HandleClientAsync(serverStream, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC Server Error]: {ex.Message}");
                await Task.Delay(500, ct);
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
}
