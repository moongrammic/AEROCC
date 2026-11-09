using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AEROCC.Core.Models;

namespace AEROCC.Core.Ipc;

public class AeroccIpcClient
{
    public async Task<IpcResponse> SendCommandAsync(IpcMessage request, int timeoutMs = 2000, CancellationToken ct = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs, ct);

            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await pipe.WriteAsync(bytes, ct);
            await pipe.FlushAsync(ct);

            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var responseLine = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return new IpcResponse { Success = false, ErrorMessage = "Пустой ответ от фоновой службы." };
            }

            var resp = JsonSerializer.Deserialize<IpcResponse>(responseLine);
            return resp ?? new IpcResponse { Success = false, ErrorMessage = "Не удалось десериализовать ответ службы." };
        }
        catch (TimeoutException)
        {
            return new IpcResponse { Success = false, ErrorMessage = "Таймаут подключения. Служба AEROCC не запущена." };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public Task<IpcResponse> GetStatusAsync(CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdGetStatus }, ct: ct);

    public Task<IpcResponse> SetBatteryLimitAsync(int percent, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetBatteryLimit, Payload = percent.ToString() }, ct: ct);

    public Task<IpcResponse> SetFanProfileAsync(FanProfile profile, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetFanProfile, Payload = ((int)profile).ToString() }, ct: ct);

    public Task<IpcResponse> SetWinLockAsync(bool locked, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetWinLock, Payload = locked.ToString() }, ct: ct);

    public Task<IpcResponse> SetFnLockAsync(bool locked, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetFnLock, Payload = locked.ToString() }, ct: ct);
}
