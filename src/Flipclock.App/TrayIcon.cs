using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Flipclock.App;

/// <summary>The always-present notification-area icon and its menu.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WindowController _window;
    readonly TaskbarIcon _icon;
    readonly ToggleMenuFlyoutItem _alwaysOnTop, _borderless, _mute;

    public event Action<bool>? MuteChanged;
    public event Action? SettingsRequested;
    public event Action? CountdownRequested;
    public event Action? ExitRequested;

    public TrayIcon(WindowController window, Window owner)
    {
        _window = window;
        var L = Strings.Current;

        _alwaysOnTop = new ToggleMenuFlyoutItem { Text = L.AlwaysOnTop, Command = new Command(() => window.SetAlwaysOnTop(!window.IsAlwaysOnTop)) };
        _borderless = new ToggleMenuFlyoutItem { Text = L.Borderless, Command = new Command(() => window.SetBorderless(!window.IsBorderless)) };
        _mute = new ToggleMenuFlyoutItem { Text = L.TrayMuteTick };
        _mute.Command = new Command(() => MuteChanged?.Invoke(!_mute.IsChecked));

        var menu = new MenuFlyout
        {
            Items =
            {
                new MenuFlyoutItem { Text = L.TrayShowHide, Command = new Command(window.ToggleVisibility) },
                new MenuFlyoutItem { Text = L.Countdown, Command = new Command(() => CountdownRequested?.Invoke()) },
                new MenuFlyoutItem { Text = L.Settings, Command = new Command(() => SettingsRequested?.Invoke()) },
                new MenuFlyoutSeparator(),
                _alwaysOnTop,
                _borderless,
                _mute,
                new MenuFlyoutSeparator(),
                new MenuFlyoutItem { Text = L.TrayExit, Command = new Command(() => ExitRequested?.Invoke()) },
            },
        };

        _icon = new TaskbarIcon
        {
            ToolTipText = "Flipclock",
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "Flipclock.ico")),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            DoubleClickCommand = new Command(window.ToggleVisibility),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Syncs the check marks with the current state.</summary>
    public void Refresh(bool soundEnabled)
    {
        _alwaysOnTop.IsChecked = _window.IsAlwaysOnTop;
        _borderless.IsChecked = _window.IsBorderless;
        _mute.IsChecked = !soundEnabled;
    }

    /// <summary>A notification balloon, silent: the caller plays its own sound.</summary>
    public void Notify(string title, string message) => _icon.ShowNotification(title, message, sound: false);

    public void Dispose() => _icon.Dispose();

    sealed class Command(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
