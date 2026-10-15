using Flipclock.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Flipclock.App;

/// <summary>
/// Owns window chrome and placement: borderless, always-on-top and fullscreen modes, the minimum size,
/// minimize-to-tray, dragging a borderless window, and saving/restoring the window rectangle.
/// </summary>
public sealed class WindowController : IDisposable
{
    const int MinWidthDip = 480;
    const int MinHeightDip = 270;

    readonly Window _window;
    readonly AppWindow _appWindow;
    readonly OverlappedPresenter _overlapped = OverlappedPresenter.Create();
    readonly Native.SubclassProc _subclass;
    RectInt32 _normalRect;

    public WindowController(Window window)
    {
        _window = window;
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Hwnd));
        _appWindow.SetPresenter(_overlapped);
        _appWindow.Changed += OnChanged;
        _subclass = WndProc;
        Native.SetWindowSubclass(Hwnd, _subclass, 1, 0);
        _normalRect = new RectInt32(_appWindow.Position.X, _appWindow.Position.Y, _appWindow.Size.Width, _appWindow.Size.Height);
    }

    public nint Hwnd { get; }
    public AppWindow AppWindow => _appWindow;
    public bool IsBorderless { get; private set; }
    public bool IsAlwaysOnTop { get; private set; }
    public bool IsFullscreen => _appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
    public bool IsVisible => _appWindow.IsVisible;

    /// <summary>Raised when the user minimizes the window; the window has already been hidden to the tray.</summary>
    public event EventHandler? MinimizedToTray;
    /// <summary>Raised when the mode or placement changed and should be persisted.</summary>
    public event EventHandler? StateChanged;

    double Scale => Native.GetDpiForWindow(Hwnd) / 96.0;

    public void Restore(ClockSettings s)
    {
        IsBorderless = s.Borderless;
        IsAlwaysOnTop = s.AlwaysOnTop;
        ApplyOverlappedChrome();

        var size = new SizeInt32(Math.Max(s.WindowWidth, MinWidthDip), Math.Max(s.WindowHeight, MinHeightDip));
        RectInt32 rect;
        if (s.WindowX is { } x && s.WindowY is { } y)
        {
            rect = new RectInt32(x, y, size.Width, size.Height);
            // A saved position on a monitor that is gone (or mostly off-screen) is moved back onto the primary display.
            if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is null || !TitleVisible(rect))
                rect = Centered(size);
        }
        else
        {
            rect = Centered(size);
        }
        _appWindow.MoveAndResize(rect);
        _normalRect = rect;

        if (s.Maximized) _overlapped.Maximize();
        if (s.Fullscreen) _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    public void Capture(ClockSettings s)
    {
        s.WindowX = _normalRect.X;
        s.WindowY = _normalRect.Y;
        s.WindowWidth = _normalRect.Width;
        s.WindowHeight = _normalRect.Height;
        s.Maximized = _overlapped.State == OverlappedPresenterState.Maximized;
        s.Fullscreen = IsFullscreen;
        s.Borderless = IsBorderless;
        s.AlwaysOnTop = IsAlwaysOnTop;
    }

    static RectInt32 Centered(SizeInt32 size)
    {
        var work = DisplayArea.Primary.WorkArea;
        int w = Math.Min(size.Width, work.Width), h = Math.Min(size.Height, work.Height);
        return new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
    }

    static bool TitleVisible(RectInt32 rect)
    {
        var top = new RectInt32(rect.X + rect.Width / 4, rect.Y, Math.Max(1, rect.Width / 2), 32);
        return DisplayArea.GetFromRect(top, DisplayAreaFallback.None) is not null;
    }

    public void SetBorderless(bool value)
    {
        IsBorderless = value;
        ApplyOverlappedChrome();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAlwaysOnTop(bool value)
    {
        IsAlwaysOnTop = value;
        ApplyOverlappedChrome();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFullscreen(bool value)
    {
        if (value == IsFullscreen) return;
        if (value)
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            _appWindow.SetPresenter(_overlapped);
            ApplyOverlappedChrome();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleFullscreen() => SetFullscreen(!IsFullscreen);

    void ApplyOverlappedChrome()
    {
        // Borderless keeps a resizable frame (so the edges still resize) but drops the title bar.
        _overlapped.SetBorderAndTitleBar(true, !IsBorderless);
        _overlapped.IsAlwaysOnTop = IsAlwaysOnTop;
    }

    public void Show()
    {
        _appWindow.Show();
        if (_overlapped.State == OverlappedPresenterState.Minimized) _overlapped.Restore();
        Native.SetForegroundWindow(Hwnd);
    }

    public void Hide() => _appWindow.Hide();

    public void ToggleVisibility()
    {
        if (_appWindow.IsVisible) Hide();
        else Show();
    }

    /// <summary>Starts a system move loop, as if the title bar had been grabbed.</summary>
    public void BeginDrag()
    {
        Native.ReleaseCapture();
        Native.SendMessage(Hwnd, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, 0);
    }

    void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((args.DidPositionChange || args.DidSizeChange)
            && sender.Presenter.Kind == AppWindowPresenterKind.Overlapped
            && _overlapped.State == OverlappedPresenterState.Restored
            && sender.IsVisible)
        {
            _normalRect = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
        }
        if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange || args.DidVisibilityChange)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        switch (msg)
        {
            case Native.WM_GETMINMAXINFO:
                unsafe
                {
                    var info = (Native.MINMAXINFO*)lParam;
                    info->MinTrackSize.X = (int)(MinWidthDip * Scale);
                    info->MinTrackSize.Y = (int)(MinHeightDip * Scale);
                }
                return 0;
            case Native.WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == Native.SC_MINIMIZE:
                // Minimizing sends the clock to the tray instead of the taskbar.
                _window.DispatcherQueue.TryEnqueue(() =>
                {
                    Hide();
                    MinimizedToTray?.Invoke(this, EventArgs.Empty);
                });
                return 0;
        }
        return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        _appWindow.Changed -= OnChanged;
        Native.RemoveWindowSubclass(Hwnd, _subclass, 1);
    }
}
