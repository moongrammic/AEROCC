using System.Runtime.InteropServices;

namespace AEROCC.Core.Audio;

public class MicrophoneManager
{
    private static MicrophoneManager? _instance;
    public static MicrophoneManager Instance => _instance ??= new MicrophoneManager();

    private const int CLSCTX_ALL = 23;
    private const int DATAFLOW_CAPTURE = 1;     // eCapture (Microphone)
    private const int ROLE_MULTIMEDIA = 1;      // eMultimedia
    private const int ROLE_CONSOLE = 0;         // eConsole

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    public event Action<bool>? MuteChanged;

    public bool GetMute()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? dev = null;
        IAudioEndpointVolume? vol = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(DATAFLOW_CAPTURE, ROLE_MULTIMEDIA, out dev);
            if (hr != 0 || dev == null)
            {
                hr = enumerator.GetDefaultAudioEndpoint(DATAFLOW_CAPTURE, ROLE_CONSOLE, out dev);
                if (hr != 0 || dev == null) return false;
            }

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            hr = dev.Activate(iid, CLSCTX_ALL, IntPtr.Zero, out var volumeObj);
            if (hr != 0 || volumeObj == null) return false;

            vol = (IAudioEndpointVolume)volumeObj;
            vol.GetMute(out bool isMuted);
            return isMuted;
        }
        catch
        {
            return false;
        }
        finally
        {
            SafeReleaseCom(vol);
            SafeReleaseCom(dev);
            SafeReleaseCom(enumerator);
        }
    }

    public bool SetMute(bool mute)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? dev = null;
        IAudioEndpointVolume? vol = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(DATAFLOW_CAPTURE, ROLE_MULTIMEDIA, out dev);
            if (hr != 0 || dev == null)
            {
                hr = enumerator.GetDefaultAudioEndpoint(DATAFLOW_CAPTURE, ROLE_CONSOLE, out dev);
                if (hr != 0 || dev == null) return false;
            }

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            hr = dev.Activate(iid, CLSCTX_ALL, IntPtr.Zero, out var volumeObj);
            if (hr != 0 || volumeObj == null) return false;

            vol = (IAudioEndpointVolume)volumeObj;
            Guid ctx = Guid.Empty;
            int setHr = vol.SetMute(mute, ref ctx);
            if (setHr == 0)
            {
                MuteChanged?.Invoke(mute);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            SafeReleaseCom(vol);
            SafeReleaseCom(dev);
            SafeReleaseCom(enumerator);
        }
    }

    private static void SafeReleaseCom(object? comObj)
    {
        if (comObj != null && Marshal.IsComObject(comObj))
        {
            try
            {
                Marshal.ReleaseComObject(comObj);
            }
            catch
            {
            }
        }
    }

    public bool ToggleMute()
    {
        bool current = GetMute();
        bool target = !current;
        return SetMute(target) ? target : current;
    }
}
