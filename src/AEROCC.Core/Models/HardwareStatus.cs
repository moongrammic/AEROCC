namespace AEROCC.Core.Models;

public record HardwareStatus
{
    public int CpuTemperature { get; init; }
    public int Fan1Rpm { get; init; }
    public int Fan2Rpm { get; init; }
    public int BatteryChargeLimit { get; init; }
    public int BatteryPolicy { get; init; }
    public bool? WinKeyLocked { get; init; }
    public bool? FnKeyLocked { get; init; }
}

public enum FanProfile
{
    Eco = 0,
    Normal = 1,
    Game = 2,
    Turbo = 3,
    Manual = 5
}
