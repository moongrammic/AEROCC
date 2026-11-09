namespace AEROCC.Core.Gpu;

public enum GpuPowerState
{
    Unknown = 0,
    Active = 1,       // dGPU is running and powered
    Sleeping = 2,     // dGPU is in low-power D3cold state (Optimus sleep)
    EcoDisabled = 3   // dGPU is powered off via BIOS/ACPI (Eco mode)
}

public enum GpuPowerMode
{
    Dynamic = 0,      // Optimus / Auto switching (recommended)
    Performance = 1,  // dGPU always kept on
    Eco = 2           // dGPU powered off, integrated Radeon only
}

public record GpuTelemetry
{
    public string Name { get; init; } = "NVIDIA GeForce RTX";
    public string IntegratedName { get; init; } = "AMD Radeon 860M";
    public GpuPowerState PowerState { get; init; } = GpuPowerState.Unknown;
    public GpuPowerMode PowerMode { get; init; } = GpuPowerMode.Dynamic;
    public int? Temperature { get; init; }
    public int? CoreUsage { get; init; }
    public int? MemoryUsedMb { get; init; }
    public int? MemoryTotalMb { get; init; }
    public double? PowerWatts { get; init; }
    public int? CoreClockMhz { get; init; }
    public string? MuxMode { get; init; }
    public bool IsNvidiaPresent { get; init; }
}
