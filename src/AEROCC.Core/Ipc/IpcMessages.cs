using AEROCC.Core.Models;

namespace AEROCC.Core.Ipc;

public static class IpcConstants
{
    public const string PipeName = "AEROCC_IPC_PIPE";

    public const string CmdGetStatus = "GetStatus";
    public const string CmdSetBatteryLimit = "SetBatteryLimit";
    public const string CmdSetFanProfile = "SetFanProfile";
    public const string CmdSetFixedFanSpeed = "SetFixedFanSpeed";
    public const string CmdSetWinLock = "SetWinLock";
    public const string CmdSetFnLock = "SetFnLock";
    public const string CmdSetApuPower = "SetApuPower";
    public const string CmdPing = "Ping";

    // Display & Keyboard Lighting
    public const string CmdSetRefreshRate = "SetRefreshRate";
    public const string CmdSetBacklightBrightness = "SetBacklightBrightness";
    public const string CmdSetBacklightEffect = "SetBacklightEffect";
    public const string CmdSetBacklightColor = "SetBacklightColor";
    public const string CmdSetCustomRgb = "SetCustomRgb";
}

public class IpcMessage
{
    public string Command { get; set; } = string.Empty;
    public string? Payload { get; set; }
}

public class IpcResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public HardwareStatus? Status { get; set; }
}
