using System.Diagnostics;
using Flipclock.Core.Clock;
using Flipclock.Core.Settings;
using Flipclock.Rendering;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.Text;

namespace Flipclock.App;

public sealed partial class MainWindow : Window
{
    readonly SettingsStore _store = new(AppContext.BaseDirectory);
    readonly ClockScene _scene = new();
    readonly FlipAnimator _animator = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly TickPlayer _ticks = new();

    ClockSettings _settings = new();
    ClockRenderer? _renderer;
    WindowController? _windowController;
    TrayIcon? _tray;

    DispatcherQueueTimer? _secondTimer, _tickTimer, _saveTimer, _cogTimer;
    bool _writable;
    bool _warnedSaveFailure;
    bool _renderingHooked;
    bool _renderQueued;
    bool _panelOpen;
    DateTime _shownDate;
    long _lastPressTicks;
    double _scaleX = 1, _scaleY = 1;

    public Strings L => Strings.Current;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void Start()
    {
        var loaded = _store.Load();
        _settings = loaded.Settings;
        _writable = _store.IsWritable();
        if (!_writable)
            ShowWarning(Strings.Current.ConfigNotWritableTitle, Strings.Current.ConfigNotWritableBody);
        else if (loaded.WasCorrupt)
            ShowWarning(Strings.Current.ConfigNotWritableTitle, Strings.Current.ConfigCorruptBody, InfoBarSeverity.Informational);

        _windowController = new WindowController(this);
        _windowController.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Flipclock.ico"));
        _windowController.AppWindow.Title = "Flipclock";
        _windowController.Restore(_settings);
        _windowController.StateChanged += (_, _) => OnWindowStateChanged();
        Closed += OnClosed;

        _renderer = CreateRenderer();
        _renderer.SetGlyphFont(_settings.DigitFontFamily, _settings.DigitFontWeight);
        if (!_renderer.SetBackgroundImage(_settings.BackgroundImage)) { /* missing file: plain background */ }
        if (!_ticks.Load(_settings.SoundFile)) { /* missing file: built-in tick */ }

        Settings.OwnerHwnd = _windowController.Hwnd;
        Settings.SetFonts(GlyphRasterizer.SystemFontFamilies());
        Settings.Load(_settings);
        WireSettingsPanel();

        _animator.SetImmediate(ClockDigits.FromTime(DateTime.Now));
        _shownDate = DateTime.Today;

        var queue = DispatcherQueue;
        _secondTimer = queue.CreateTimer();
        _secondTimer.IsRepeating = false;
        _secondTimer.Tick += (_, _) => OnSecond();
        _tickTimer = queue.CreateTimer();
        _tickTimer.IsRepeating = false;
        _tickTimer.Tick += (_, _) => PlayTick();
        _saveTimer = queue.CreateTimer();
        _saveTimer.IsRepeating = false;
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.Tick += (_, _) => SaveNow();
        _cogTimer = queue.CreateTimer();
        _cogTimer.IsRepeating = false;
        _cogTimer.Interval = TimeSpan.FromSeconds(3);
        _cogTimer.Tick += (_, _) => { if (!_panelOpen) CogButton.Opacity = 0; };
        ScheduleNextSecond();
        _cogTimer.Start();

        _tray = new TrayIcon(_windowController, this);
        _tray.MuteChanged += muted => { _settings.SoundEnabled = !muted; Settings.Load(_settings); ScheduleSave(); };
        _tray.SettingsRequested += () => { _windowController.Show(); OpenPanel(); };
        _tray.ExitRequested += Close;
        _windowController.MinimizedToTray += (_, _) => _tray.Refresh(_settings.SoundEnabled);
        _tray.Refresh(_settings.SoundEnabled);

        UpdateDate();
        Activate();
        if (!loaded.FileExisted && _writable) SaveNow();
    }

    ClockRenderer CreateRenderer()
    {
        var renderer = new ClockRenderer(Math.Max(1, (int)(ClockPanel.ActualWidth * _scaleX)), Math.Max(1, (int)(ClockPanel.ActualHeight * _scaleY)));
        Native.SetSwapChain(ClockPanel, renderer.SwapChainPointer);
        return renderer;
    }

    // ---------------------------------------------------------------- time & animation

    void ScheduleNextSecond()
    {
        var now = DateTime.Now;
        // Fire just after the next second boundary.
        _secondTimer!.Interval = TimeSpan.FromMilliseconds(1000 - now.Millisecond + 3);
        _secondTimer.Start();
    }

    FlipTiming Timing => new(_settings.FlipDurationMs, _settings.CascadeDelayMs, _settings.Bounce);

    void OnSecond()
    {
        var now = DateTime.Now;
        StartFlip(ClockDigits.FromTime(now), flipAll: false);
        if (now.Date != _shownDate)
        {
            _shownDate = now.Date;
            UpdateDate();
        }
        ScheduleNextSecond();
    }

    void StartFlip(int[] digits, bool flipAll)
    {
        double nowMs = _clock.Elapsed.TotalMilliseconds;
        var landing = _animator.Begin(digits, nowMs, Timing, flipAll);
        if (landing is { } at)
        {
            _tickTimer!.Stop();
            _tickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, at - nowMs));
            _tickTimer.Start();
        }
        HookRendering();
    }

    void PlayTick()
    {
        if (_settings.SoundEnabled) _ticks.Play(_settings.Volume);
    }

    void HookRendering()
    {
        if (_renderingHooked) return;
        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    void OnRendering(object? sender, object e)
    {
        double nowMs = _clock.Elapsed.TotalMilliseconds;
        RenderFrame(nowMs);
        if (!_animator.IsAnimating(nowMs))
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
            RenderFrame(_clock.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>Renders once soon, coalescing multiple requests (used for static changes).</summary>
    void RequestRender()
    {
        if (_renderingHooked || _renderQueued) return;
        _renderQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            _renderQueued = false;
            if (!_renderingHooked) RenderFrame(_clock.Elapsed.TotalMilliseconds);
        });
    }

    void RenderFrame(double nowMs)
    {
        if (_renderer is null || _windowController is { IsVisible: false }) return;
        _scene.ApplySettings(_settings, _scaleX);
        for (int i = 0; i < ClockDigits.Count; i++) _scene.Tiles[i] = _animator.GetVisual(i, nowMs);
        if (!_renderer.Render(_scene))
        {
            // Device lost (driver update, GPU reset): rebuild everything.
            _renderer.Dispose();
            _renderer = CreateRenderer();
            _renderer.Resize((int)(ClockPanel.ActualWidth * _scaleX), (int)(ClockPanel.ActualHeight * _scaleY), (float)_scaleX, (float)_scaleY);
            _renderer.SetGlyphFont(_settings.DigitFontFamily, _settings.DigitFontWeight);
            _renderer.SetBackgroundImage(_settings.BackgroundImage);
        }
    }

    // ---------------------------------------------------------------- layout

    void OnPanelSizeChanged(object sender, SizeChangedEventArgs e) => ResizeRenderer();

    void OnCompositionScaleChanged(SwapChainPanel sender, object args) => ResizeRenderer();

    void ResizeRenderer()
    {
        _scaleX = ClockPanel.CompositionScaleX;
        _scaleY = ClockPanel.CompositionScaleY;
        _renderer?.Resize((int)Math.Ceiling(ClockPanel.ActualWidth * _scaleX), (int)Math.Ceiling(ClockPanel.ActualHeight * _scaleY), (float)_scaleX, (float)_scaleY);
        UpdateDate();
        RenderFrame(_clock.Elapsed.TotalMilliseconds);
    }

    void UpdateDate()
    {
        var s = _settings;
        DateText.Visibility = s.DateVisible ? Visibility.Visible : Visibility.Collapsed;
        if (!s.DateVisible) return;

        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var layout = ClockLayout.Compute(w, h, s.Margin, s.SecondsScale, s.DateSize);

        DateText.Text = DateFormatter.Format(DateTime.Now, s.DateLanguage);
        DateText.FontFamily = new FontFamily(s.DateFontFamily);
        DateText.FontWeight = new FontWeight((ushort)s.DateFontWeight);
        DateText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, s.DateColor.R, s.DateColor.G, s.DateColor.B));
        DateText.FontSize = layout.DateFontSize;
        DateText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        DateText.LineHeight = layout.DateFontSize * 1.3;
        double top = layout.DateCenterY - layout.DateFontSize * 0.65;
        double pad = layout.DateSidePadding;
        (DateText.HorizontalAlignment, DateText.Margin) = s.DatePosition switch
        {
            DatePosition.Left => (HorizontalAlignment.Left, new Thickness(pad, top, 0, 0)),
            DatePosition.Right => (HorizontalAlignment.Right, new Thickness(0, top, pad, 0)),
            _ => (HorizontalAlignment.Center, new Thickness(0, top, 0, 0)),
        };
    }

    // ---------------------------------------------------------------- settings

    void WireSettingsPanel()
    {
        Settings.Changed += OnSettingsChanged;
        Settings.CloseRequested += ClosePanel;
        Settings.TestFlipRequested += () => StartFlip([.. _animator.Digits], flipAll: true);
        Settings.TestTickRequested += () => _ticks.Play(_settings.Volume);
        Settings.ResetRequested += () =>
        {
            _settings.CopyAppearanceFrom(new ClockSettings());
            Settings.Load(_settings);
            OnSettingsChanged(SettingsChange.Layout | SettingsChange.DigitFont | SettingsChange.BackgroundImage | SettingsChange.Sound | SettingsChange.Date);
        };
        Settings.AlwaysOnTopChanged += on => _windowController!.SetAlwaysOnTop(on);
        Settings.BorderlessChanged += on => _windowController!.SetBorderless(on);
        Settings.FullscreenChanged += on => _windowController!.SetFullscreen(on);
    }

    void OnSettingsChanged(SettingsChange change)
    {
        if (change.HasFlag(SettingsChange.DigitFont))
            _renderer?.SetGlyphFont(_settings.DigitFontFamily, _settings.DigitFontWeight);
        if (change.HasFlag(SettingsChange.BackgroundImage))
            _renderer?.SetBackgroundImage(_settings.BackgroundImage);
        if (change.HasFlag(SettingsChange.Sound))
            _ticks.Load(_settings.SoundFile);
        if (change.HasFlag(SettingsChange.Date) || change.HasFlag(SettingsChange.Layout))
            UpdateDate();
        _tray?.Refresh(_settings.SoundEnabled);
        RequestRender();
        ScheduleSave();
    }

    void OnWindowStateChanged()
    {
        var wc = _windowController!;
        Settings.SetWindowState(wc.IsAlwaysOnTop, wc.IsBorderless, wc.IsFullscreen);
        _tray?.Refresh(_settings.SoundEnabled);
        if (wc.IsVisible) RequestRender();
        ScheduleSave();
    }

    void ScheduleSave()
    {
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    void SaveNow()
    {
        _saveTimer?.Stop();
        _windowController?.Capture(_settings);
        if (!_writable) return;
        if (!_store.TrySave(_settings) && !_warnedSaveFailure)
        {
            _warnedSaveFailure = true;
            ShowWarning(Strings.Current.ConfigNotWritableTitle, Strings.Current.ConfigNotWritableBody);
        }
    }

    void ShowWarning(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Warning)
    {
        WarningBar.Title = title;
        WarningBar.Message = message;
        WarningBar.Severity = severity;
        WarningBar.IsOpen = true;
    }

    // ---------------------------------------------------------------- panel & cog

    void OnCogClick(object sender, RoutedEventArgs e)
    {
        if (_panelOpen) ClosePanel();
        else OpenPanel();
    }

    void OpenPanel()
    {
        if (_panelOpen) return;
        _panelOpen = true;
        CogButton.Opacity = 1;
        Settings.SetWindowState(_windowController!.IsAlwaysOnTop, _windowController.IsBorderless, _windowController.IsFullscreen);
        Settings.Visibility = Visibility.Visible;
        CogButton.Visibility = Visibility.Collapsed;
        AnimatePanel(0, null);
    }

    void ClosePanel()
    {
        if (!_panelOpen) return;
        _panelOpen = false;
        AnimatePanel(Settings.Width, () =>
        {
            if (!_panelOpen) Settings.Visibility = Visibility.Collapsed;
        });
        CogButton.Visibility = Visibility.Visible;
        _cogTimer?.Start();
    }

    void AnimatePanel(double to, Action? completed)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, SettingsShift);
        Storyboard.SetTargetProperty(animation, "X");
        var storyboard = new Storyboard { Children = { animation } };
        if (completed is not null) storyboard.Completed += (_, _) => completed();
        storyboard.Begin();
    }

    void OnPointerActivity(object sender, PointerRoutedEventArgs e)
    {
        CogButton.Opacity = 1;
        _cogTimer?.Stop();
        _cogTimer?.Start();
    }

    // ---------------------------------------------------------------- input

    void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource != InputLayer) return;
        var point = e.GetCurrentPoint(InputLayer);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (_panelOpen)
        {
            ClosePanel();
            return;
        }

        var wc = _windowController!;
        if (wc.IsBorderless && !wc.IsFullscreen)
        {
            // The move loop swallows the second click, so double-clicks are detected here.
            long now = Environment.TickCount64;
            if (now - _lastPressTicks <= Native.GetDoubleClickTime())
            {
                _lastPressTicks = 0;
                wc.ToggleFullscreen();
                return;
            }
            _lastPressTicks = now;
            wc.BeginDrag();
        }
    }

    void OnInputDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var wc = _windowController!;
        if (wc.IsBorderless && !wc.IsFullscreen) return; // handled in OnRootPointerPressed
        wc.ToggleFullscreen();
    }

    void OnFullscreenAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _windowController?.ToggleFullscreen();
        args.Handled = true;
    }

    void OnBorderlessAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_windowController is { } wc) wc.SetBorderless(!wc.IsBorderless);
        args.Handled = true;
    }

    void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_panelOpen) ClosePanel();
        else if (_windowController is { IsFullscreen: true } wc) wc.SetFullscreen(false);
        args.Handled = true;
    }

    // ---------------------------------------------------------------- shutdown

    void OnClosed(object sender, WindowEventArgs args)
    {
        SaveNow();
        _secondTimer?.Stop();
        _tickTimer?.Stop();
        if (_renderingHooked) CompositionTarget.Rendering -= OnRendering;
        _tray?.Dispose();
        _ticks.Dispose();
        _renderer?.Dispose();
        _windowController?.Dispose();
        Application.Current.Exit();
    }
}
