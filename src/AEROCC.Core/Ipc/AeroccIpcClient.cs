using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AEROCC.Core.Models;

namespace AEROCC.Core.Ipc;

public class AeroccIpcClient
{
    public bool IsServiceAvailable { get; private set; }

    public async Task<IpcResponse> SendCommandAsync(IpcMessage request, int timeoutMs = 120, CancellationToken ct = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs, ct);

            IsServiceAvailable = true;

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
            IsServiceAvailable = false;
            return new IpcResponse { Success = false, ErrorMessage = "Таймаут подключения. Служба AEROCC не запущена." };
        }
        catch (IOException)
        {
            IsServiceAvailable = false;
            return new IpcResponse { Success = false, ErrorMessage = "Канал связи разорван." };
        }
        catch (Exception ex)
        {
            IsServiceAvailable = false;
            return new IpcResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public Task<IpcResponse> GetStatusAsync(CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdGetStatus }, timeoutMs: 120, ct: ct);

    public Task<IpcResponse> SetBatteryLimitAsync(int percent, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetBatteryLimit, Payload = percent.ToString() }, ct: ct);

    public Task<IpcResponse> SetFanProfileAsync(FanProfile profile, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetFanProfile, Payload = ((int)profile).ToString() }, ct: ct);

    public Task<IpcResponse> SetWinLockAsync(bool locked, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetWinLock, Payload = locked.ToString() }, ct: ct);

    public Task<IpcResponse> SetFnLockAsync(bool locked, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetFnLock, Payload = locked.ToString() }, ct: ct);

    public Task<IpcResponse> SetRefreshRateAsync(int hz, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetRefreshRate, Payload = hz.ToString() }, ct: ct);

    public Task<IpcResponse> SetBacklightBrightnessAsync(byte brightness, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetBacklightBrightness, Payload = brightness.ToString() }, ct: ct);

    public Task<IpcResponse> SetBacklightColorAsync(KeyboardColor color, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetBacklightColor, Payload = ((byte)color).ToString() }, ct: ct);

    public Task<IpcResponse> SetBacklightEffectAsync(KeyboardEffect effect, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetBacklightEffect, Payload = ((byte)effect).ToString() }, ct: ct);

    public Task<IpcResponse> SetCustomRgbAsync(byte r, byte g, byte b, byte brightness = 100, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetCustomRgb, Payload = $"{r},{g},{b},{brightness}" }, ct: ct);

    public Task<IpcResponse> SetGpuPowerModeAsync(AEROCC.Core.Gpu.GpuPowerMode mode, CancellationToken ct = default) =>
        SendCommandAsync(new IpcMessage { Command = IpcConstants.CmdSetGpuPowerMode, Payload = ((int)mode).ToString() }, ct: ct);
}
