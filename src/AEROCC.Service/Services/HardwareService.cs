using AEROCC.Core.Ipc;
using AEROCC.Core.Models;
using AEROCC.Core.Wmi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AEROCC.Service.Services;

public class HardwareService : IHostedService, IDisposable
{
    private readonly ILogger<HardwareService> _logger;
    private readonly GigabyteWmiClient _wmiClient;
    private AeroccIpcServer? _ipcServer;
    private CancellationTokenSource? _workerCts;
    private HardwareStatus _latestStatus = new();

    public HardwareService(ILogger<HardwareService> logger)
    {
        _logger = logger;
        _wmiClient = new GigabyteWmiClient();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AEROCC Background Service starting...");

        if (!GigabyteWmiMofRegistrar.EnsureRegistered())
        {
            _logger.LogWarning("ACPI WMI schema (GB_WMIACPI_*) could not be verified or registered on startup.");
        }

        // Arm the Watchdog
        _logger.LogInformation("[Watchdog] Initialized. Fans will auto-revert to BIOS on service shutdown.");

        // Initial hardware check
        try
        {
            _wmiClient.Keyboard.Initialize();
            _latestStatus = _wmiClient.GetHardwareStatus();
            _logger.LogInformation("Initial hardware status: CPU={CpuTemp}°C, Fan1={Fan1}RPM, Fan2={Fan2}RPM, BatteryLimit={Limit}%, Display={Disp}@{Hz}Hz",
                _latestStatus.CpuTemperature, _latestStatus.Fan1Rpm, _latestStatus.Fan2Rpm, _latestStatus.BatteryChargeLimit,
                _latestStatus.DisplayResolution ?? "Unknown", _latestStatus.DisplayRefreshRate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get initial hardware status");
        }

        // Start IPC Named Pipe Server
        _ipcServer = new AeroccIpcServer(_wmiClient, () => _latestStatus);
        _ipcServer.Start();
        _logger.LogInformation("IPC Server listening on pipe: {PipeName}", IpcConstants.PipeName);

        // Start monitoring loop (every 1.5 seconds)
        _workerCts = new CancellationTokenSource();
        _ = MonitorLoopAsync(_workerCts.Token);

        return Task.CompletedTask;
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, ct);
                _latestStatus = _wmiClient.GetHardwareStatus();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in hardware monitoring cycle");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AEROCC Service stopping...");

        // Stop polling and IPC
        _workerCts?.Cancel();
        _ipcServer?.Stop();

        // Watchdog trigger: revert fans to BIOS Auto control
        RevertFansToBiosAuto();

        return Task.CompletedTask;
    }

    private void RevertFansToBiosAuto()
    {
        try
        {
            _logger.LogInformation("[Watchdog Triggered] Restoring fans to BIOS Auto control...");
            _wmiClient.ClearCustomApuLimits();
            bool restored = _wmiClient.SetFanProfile(FanProfile.Normal);
            if (restored)
                _logger.LogInformation("[Watchdog] WMI accepted all BIOS Auto restore writes.");
            else
                _logger.LogWarning("[Watchdog] A BIOS Auto restore write failed. Fan control state is unknown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] Failed to restore BIOS fan control");
        }
    }

    public void Dispose()
    {
        _workerCts?.Cancel();
        _workerCts?.Dispose();
        _ipcServer?.Dispose();
        _wmiClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
