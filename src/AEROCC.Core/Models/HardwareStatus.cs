using System.Text.Json.Serialization;
using AEROCC.Core.Gpu;

namespace AEROCC.Core.Models;

public record HardwareStatus
{
    // Omit unavailable readings so older IPC clients can still deserialize the status.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CpuTemperature { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Fan1Rpm { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Fan2Rpm { get; init; }
    public DateTimeOffset? ThermalSampledAtUtc { get; init; }
    public int BatteryChargeLimit { get; init; }
    public int BatteryPolicy { get; init; }
    public bool? WinKeyLocked { get; init; }
    public bool? FnKeyLocked { get; init; }

    // Fan Profile
    public FanProfile ActiveFanProfile { get; init; } = FanProfile.Normal;

    // Display Information
    public int DisplayRefreshRate { get; init; }
    public string? DisplayResolution { get; init; }
    public List<int> AvailableRefreshRates { get; init; } = new();

    // Keyboard Backlight Information
    public int BacklightBrightness { get; init; }
    public string? BacklightEffect { get; init; }
    public string? BacklightColor { get; init; }

    // GPU Information
    public GpuTelemetry? Gpu { get; init; }

    // Power Telemetry
    public double? CpuPowerWatts { get; init; }
    public double? BatteryChargeWatts { get; init; }
    public double? BatteryDischargeWatts { get; init; }
    public bool IsAcOnline { get; init; }
    public bool IsEnergySaverEnabled { get; init; }

    public bool HasFreshThermalSample()
    {
        if (ThermalSampledAtUtc is not { } sampledAt)
            return false;

        var age = DateTimeOffset.UtcNow - sampledAt;
        return age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(5);
    }
}

public enum FanProfile
{
    Eco = 0,
    Normal = 1,
    Game = 2,
    Turbo = 3,
    Manual = 5
}
