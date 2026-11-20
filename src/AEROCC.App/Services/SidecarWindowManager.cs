using System.Windows;

namespace AEROCC.App.Services;

public enum SidecarModule
{
    None,
    BacklightStudio,
    FanCurve,
    ThermalMonitor
}

public class SidecarWindowManager
{
    private static SidecarWindowManager? _instance;
    public static SidecarWindowManager Instance => _instance ??= new SidecarWindowManager();

    private Window? _owner;
    private Window? _activeSatellite;
    private SidecarModule _activeModule = SidecarModule.None;

    public SidecarModule ActiveModule => _activeModule;
    public bool IsAnyOpen => _activeSatellite != null && _activeSatellite.IsVisible;

    public event Action<SidecarModule>? ModuleChanged;

    public void RegisterOwner(Window owner)
    {
        if (_owner != null)
        {
            _owner.LocationChanged -= OnOwnerMoved;
            _owner.IsVisibleChanged -= OnOwnerVisibilityChanged;
            _owner.Closed -= OnOwnerClosed;
        }

        _owner = owner;
        _owner.LocationChanged += OnOwnerMoved;
        _owner.IsVisibleChanged += OnOwnerVisibilityChanged;
        _owner.Closed += OnOwnerClosed;
    }

    public void ToggleModule<T>(SidecarModule module, Func<T> factory) where T : Window
    {
        if (_owner == null) return;

        if (_activeModule == module && _activeSatellite != null && _activeSatellite.IsVisible)
        {
            // Toggle off
            CloseCurrent();
            return;
        }

        CloseCurrent();

        var window = factory();
        _activeSatellite = window;
        _activeModule = module;

        window.Owner = _owner;
        window.Closed += (s, e) =>
        {
            if (_activeSatellite == window)
            {
                _activeSatellite = null;
                _activeModule = SidecarModule.None;
                ModuleChanged?.Invoke(SidecarModule.None);
            }
        };

        UpdatePosition(window);
        window.Show();
        window.Activate();

        ModuleChanged?.Invoke(module);
    }

    public void CloseCurrent()
    {
        if (_activeSatellite != null)
        {
            var temp = _activeSatellite;
            _activeSatellite = null;
            _activeModule = SidecarModule.None;
            try
            {
                temp.Close();
            }
            catch { }
            ModuleChanged?.Invoke(SidecarModule.None);
        }
    }

    public void HideAll()
    {
        if (_activeSatellite != null && _activeSatellite.IsVisible)
        {
            _activeSatellite.Hide();
        }
    }

    public void ShowAll()
    {
        if (_activeSatellite != null && !_activeSatellite.IsVisible)
        {
            UpdatePosition(_activeSatellite);
            _activeSatellite.Show();
        }
    }

    public void UpdatePosition(Window? window = null)
    {
        var target = window ?? _activeSatellite;
        if (target == null || _owner == null) return;

        var workArea = SystemParameters.WorkArea;

        // Position directly to the left of the owner window
        double targetLeft = _owner.Left - target.Width - 10;
        if (targetLeft < workArea.Left)
        {
            targetLeft = workArea.Left + 10;
        }

        // Align vertically with owner
        double targetTop = _owner.Top;
        if (targetTop + target.Height > workArea.Bottom)
        {
            targetTop = workArea.Bottom - target.Height - 12;
        }
        if (targetTop < workArea.Top)
        {
            targetTop = workArea.Top + 12;
        }

        target.Left = targetLeft;
        target.Top = targetTop;
    }

    private void OnOwnerMoved(object? sender, EventArgs e)
    {
        UpdatePosition();
    }

    private void OnOwnerVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_owner == null) return;
        if (_owner.IsVisible)
        {
            if (_activeSatellite != null)
            {
                UpdatePosition(_activeSatellite);
                _activeSatellite.Show();
            }
        }
        else
        {
            if (_activeSatellite != null)
            {
                _activeSatellite.Hide();
            }
        }
    }

    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        CloseCurrent();
    }
}
