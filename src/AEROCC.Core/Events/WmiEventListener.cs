using System.Management;

namespace AEROCC.Core.Events;

public class WmiEventListener : IDisposable
{
    private static WmiEventListener? _instance;
    public static WmiEventListener Instance => _instance ??= new WmiEventListener();

    private ManagementEventWatcher? _watcher;
    private bool _isStarted = false;

    public event Action<bool>? FnLockChanged;
    public event Action<bool>? TouchpadChanged;
    public event Action<bool>? WinLockChanged;
    public event Action<bool>? AirplaneModeChanged;
    public event Action<byte[]>? RawEventArrived;

    public bool IsStarted => _isStarted;

    public void Start()
    {
        if (_isStarted) return;

        try
        {
            var scope = new ManagementScope(@"\\localhost\root\WMI");
            scope.Connect();

            var query = new EventQuery("SELECT * FROM GB_WMIACPI_Event");
            _watcher = new ManagementEventWatcher(scope, query);
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();
            _isStarted = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WmiEventListener] Failed to start: {ex.Message}");
        }
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent.Properties["Data"]?.Value is not Array rawArray)
            {
                return;
            }

            byte[] data = new byte[rawArray.Length];
            for (int i = 0; i < rawArray.Length; i++)
            {
                data[i] = Convert.ToByte(rawArray.GetValue(i));
            }

            if (data.Length == 0) return;

            RawEventArrived?.Invoke(data);

            switch (data[0])
            {
                case 201: // FnLock
                    bool fnLockOn = data.Length > 1 && data[1] == 1;
                    FnLockChanged?.Invoke(fnLockOn);
                    break;

                case 202: // Touchpad
                    bool touchpadOn = data.Length > 1 && data[1] == 1;
                    TouchpadChanged?.Invoke(touchpadOn);
                    break;

                case 203: // WinLock
                    bool winLockOn = data.Length > 1 && data[1] == 1;
                    WinLockChanged?.Invoke(winLockOn);
                    break;

                case 207: // Airplane mode
                    bool airplaneActive = Radio.AirplaneModeManager.Instance.IsAirplaneModeActive();
                    AirplaneModeChanged?.Invoke(airplaneActive);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WmiEventListener] Error parsing event: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_isStarted) return;

        try
        {
            _watcher?.Stop();
            _watcher?.Dispose();
            _watcher = null;
        }
        catch
        {
        }
        finally
        {
            _isStarted = false;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
