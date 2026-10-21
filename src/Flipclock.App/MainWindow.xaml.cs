using System.Diagnostics;
using Flipclock.Core.Clock;
using Flipclock.Core.Settings;
using Flipclock.Core.Timers;
using Flipclock.Rendering;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Polygon = Microsoft.UI.Xaml.Shapes.Polygon;
using VirtualKey = Windows.System.VirtualKey;
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

    // Countdowns live for the session only: nothing about them is saved.
    readonly CountdownCollection _countdowns = new();
    readonly RepeatButton[] _upArrows = new RepeatButton[ClockDigits.Count];
    readonly RepeatButton[] _downArrows = new RepeatButton[ClockDigits.Count];
    Countdown? _countdown;
    CountdownLayoutResult? _countdownLayout;
    bool _countdownMode;
    bool _blinkOff;
    int _selectedTile = -1;

    DispatcherQueueTimer? _secondTimer, _tickTimer, _saveTimer, _cogTimer, _countdownTimer, _blinkTimer;
    bool _writable;
    bool _warnedSaveFailure;
    bool _renderingHooked;
    bool _renderQueued;
    bool _panelOpen;
    Storyboard? _panelStoryboard;
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
        _cogTimer.Tick += (_, _) => { if (!_panelOpen) TopButtons.Opacity = 0; };
        _countdownTimer = queue.CreateTimer();
        _countdownTimer.IsRepeating = false;
        _countdownTimer.Tick += (_, _) => OnCountdownTick();
        _blinkTimer = queue.CreateTimer();
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(500);
        _blinkTimer.Tick += (_, _) => { _blinkOff = !_blinkOff; RequestRender(); };
        BuildArrows();
        ScheduleNextSecond();
        _cogTimer.Start();

        _tray = new TrayIcon(_windowController, this);
        _tray.MuteChanged += muted => { _settings.SoundEnabled = !muted; Settings.Load(_settings); ScheduleSave(); };
        _tray.SettingsRequested += () => { _windowController.Show(); OpenPanel(); };
        _tray.CountdownRequested += () => { _windowController.Show(); EnterCountdown(); };
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
        if (!_countdownMode) StartFlip(ClockDigits.FromTime(now), flipAll: false);
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
        if (_countdownMode && _countdownLayout is { } countdownLayout) _scene.MarginPx = countdownLayout.Margin * _scaleX;
        // A finished countdown blinks by drawing its digits in the tile color.
        if (_blinkOff) _scene.DigitColor = _scene.TileColor;
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
        LayoutCountdown();
        RenderFrame(_clock.Elapsed.TotalMilliseconds);
    }

    void UpdateDate()
    {
        var s = _settings;
        bool visible = s.DateVisible && !_countdownMode;
        DateText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        double w = ClockArea.ActualWidth, h = ClockArea.ActualHeight;
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
        {
            UpdateDate();
            LayoutCountdown();
        }
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
        TopButtons.Opacity = 1;
        Settings.SetWindowState(_windowController!.IsAlwaysOnTop, _windowController.IsBorderless, _windowController.IsFullscreen);
        Settings.Visibility = Visibility.Visible;
        CogButton.Visibility = Visibility.Collapsed;
        AnimatePanel(open: true, null);
    }

    void ClosePanel()
    {
        if (!_panelOpen) return;
        _panelOpen = false;
        AnimatePanel(open: false, () =>
        {
            if (!_panelOpen) Settings.Visibility = Visibility.Collapsed;
        });
        CogButton.Visibility = Visibility.Visible;
        _cogTimer?.Start();
    }

    /// <summary>
    /// Opens the panel's column (pushing the clock left) while sliding the panel in, or the reverse.
    /// </summary>
    void AnimatePanel(bool open, Action? completed)
    {
        // Stop() reverts to the base values, so pin them to where a running animation currently is.
        double current = SettingsHost.ActualWidth;
        _panelStoryboard?.Stop();
        SettingsHost.Width = current;
        SettingsShift.X = Settings.Width - current;

        double width = open ? Settings.Width : 0;
        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var hostAnimation = new DoubleAnimation { To = width, Duration = duration, EasingFunction = easing, EnableDependentAnimation = true };
        Storyboard.SetTarget(hostAnimation, SettingsHost);
        Storyboard.SetTargetProperty(hostAnimation, "Width");
        var shiftAnimation = new DoubleAnimation { To = Settings.Width - width, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(shiftAnimation, SettingsShift);
        Storyboard.SetTargetProperty(shiftAnimation, "X");

        var storyboard = new Storyboard { Children = { hostAnimation, shiftAnimation } };
        storyboard.Completed += (_, _) =>
        {
            if (_panelStoryboard != storyboard) return;
            _panelStoryboard = null;
            storyboard.Stop();
            SettingsHost.Width = width;
            SettingsShift.X = Settings.Width - width;
            completed?.Invoke();
        };
        _panelStoryboard = storyboard;
        storyboard.Begin();
    }

    void OnPointerActivity(object sender, PointerRoutedEventArgs e)
    {
        TopButtons.Opacity = 1;
        _cogTimer?.Stop();
        _cogTimer?.Start();
    }

    // ---------------------------------------------------------------- countdown

    void OnCountdownClick(object sender, RoutedEventArgs e)
    {
        if (_countdownMode) ExitCountdown();
        else EnterCountdown();
    }

    void EnterCountdown()
    {
        if (_countdownMode) return;
        _countdownMode = true;
        _countdown ??= _countdowns.Add();
        CountdownLayer.Visibility = Visibility.Visible;
        CountdownIcon.Glyph = "";
        ToolTipService.SetToolTip(CountdownButton, L.BackToClock);
        AutomationProperties.SetName(CountdownButton, L.BackToClock);
        DescriptionBox.Text = _countdown.Description;
        UpdateDate();
        LayoutCountdown();
        RefreshCountdown();
        StartFlip(_countdown.Digits(_clock.Elapsed.TotalMilliseconds), flipAll: false);
        RequestRender();
    }

    /// <summary>Back to the clock; running countdowns keep going in the background.</summary>
    void ExitCountdown()
    {
        if (!_countdownMode) return;
        _countdownMode = false;
        CountdownLayer.Visibility = Visibility.Collapsed;
        CountdownIcon.Glyph = "";
        ToolTipService.SetToolTip(CountdownButton, L.Countdown);
        AutomationProperties.SetName(CountdownButton, L.Countdown);
        _selectedTile = -1;
        RefreshCountdown();
        UpdateDate();
        StartFlip(ClockDigits.FromTime(DateTime.Now), flipAll: false);
        RequestRender();
    }

    void StartCountdown()
    {
        if (_countdown is not { CanStart: true }) return;
        _countdown.Start(_clock.Elapsed.TotalMilliseconds);
        CountdownButton.Focus(FocusState.Programmatic);
        ScheduleCountdown();
        RefreshCountdown();
    }

    void OnStartClick(object sender, RoutedEventArgs e) => StartCountdown();

    void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_countdown is null) return;
        double nowMs = _clock.Elapsed.TotalMilliseconds;
        if (_countdown.State == CountdownState.Running) _countdown.Pause(nowMs);
        else _countdown.Resume(nowMs);
        ScheduleCountdown();
        RefreshCountdown();
    }

    void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (_countdown is null) return;
        _countdown.Reset();
        ScheduleCountdown();
        RefreshCountdown();
        StartFlip(_countdown.Digits(_clock.Elapsed.TotalMilliseconds), flipAll: false);
    }

    void OnDescriptionChanged(object sender, TextChangedEventArgs e)
    {
        if (_countdown is not null) _countdown.Description = DescriptionBox.Text;
    }

    void OnDescriptionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        StartCountdown();
        e.Handled = true;
    }

    /// <summary>Wakes up when the next displayed digit of any running countdown changes.</summary>
    void ScheduleCountdown()
    {
        _countdownTimer!.Stop();
        double nowMs = _clock.Elapsed.TotalMilliseconds;
        if (_countdowns.NextChangeMs(nowMs) is not { } next) return;
        _countdownTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, next - nowMs + 3));
        _countdownTimer.Start();
    }

    void OnCountdownTick()
    {
        double nowMs = _clock.Elapsed.TotalMilliseconds;
        var finished = _countdowns.Update(nowMs);
        if (_countdownMode && _countdown is not null) StartFlip(_countdown.Digits(nowMs), flipAll: false);
        foreach (var c in finished) OnCountdownFinished(c);
        if (finished.Count > 0) RefreshCountdown();
        ScheduleCountdown();
    }

    void OnCountdownFinished(Countdown c)
    {
        // The alarm ignores "mute tick": muting is about the ticking, not about missing the end.
        _ticks.PlayAlarm(_settings.Volume);
        if (_windowController is { IsVisible: false })
        {
            string body = c.Description.Length > 0 ? c.Description : Format(c.Duration);
            _tray?.Notify(L.CountdownFinished, body);
        }
        else if (c == _countdown)
        {
            EnterCountdown();
        }
    }

    static string Format(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>Syncs buttons, arrows, description and blinking with the state of the shown countdown.</summary>
    void RefreshCountdown()
    {
        var state = _countdown?.State ?? CountdownState.Idle;
        bool idle = state == CountdownState.Idle;
        StartButton.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = _countdown is { CanStart: true };
        ResetButton.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        PauseButton.Visibility = state is CountdownState.Running or CountdownState.Paused ? Visibility.Visible : Visibility.Collapsed;
        bool paused = state == CountdownState.Paused;
        PauseIcon.Glyph = paused ? "" : "";
        PauseLabel.Text = paused ? L.Continue : L.Pause;
        AutomationProperties.SetName(PauseButton, PauseLabel.Text);

        string description = _countdown?.Description ?? "";
        DescriptionBox.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        DescriptionText.Text = description;
        DescriptionText.Visibility = !idle && description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ArrowCanvas.Visibility = idle && _countdownLayout is { ArrowsFit: true } ? Visibility.Visible : Visibility.Collapsed;
        PlaceHighlight();

        bool blink = _countdownMode && state == CountdownState.Finished;
        if (blink && !_blinkTimer!.IsRunning) _blinkTimer.Start();
        if (!blink && _blinkTimer!.IsRunning)
        {
            _blinkTimer.Stop();
            _blinkOff = false;
            RequestRender();
        }
    }

    void BuildArrows()
    {
        for (int i = 0; i < ClockDigits.Count; i++)
        {
            _upArrows[i] = CreateArrow(i, +1);
            _downArrows[i] = CreateArrow(i, -1);
        }
    }

    RepeatButton CreateArrow(int tile, int delta)
    {
        var triangle = new Polygon
        {
            // Unit triangle, stretched to the button: pointing up or down.
            Points = delta > 0 ? [new(0, 1), new(0.5, 0), new(1, 1)] : [new(0, 0), new(0.5, 1), new(1, 0)],
            Stretch = Stretch.Fill,
        };
        var button = new RepeatButton
        {
            Content = triangle,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Delay = 400,
            Interval = 110,
            IsTabStop = false,
        };
        AutomationProperties.SetName(button, $"{(delta > 0 ? L.DigitUp : L.DigitDown)} {tile + 1}");
        button.Click += (_, _) => StepTile(tile, delta);
        ArrowCanvas.Children.Add(button);
        return button;
    }

    void StepTile(int tile, int delta)
    {
        if (_countdown is not { State: CountdownState.Idle }) return;
        _countdown.StepDigit(tile, delta);
        StartFlip(_countdown.Digits(_clock.Elapsed.TotalMilliseconds), flipAll: false);
        RefreshCountdown();
    }

    /// <summary>Places arrows, description and controls around the tiles, with a margin wide enough for the arrows.</summary>
    void LayoutCountdown()
    {
        if (!_countdownMode) return;
        double w = ClockArea.ActualWidth, h = ClockArea.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var s = _settings;
        var l = CountdownLayout.Compute(w, h, s.Margin, s.SecondsScale, s.DateSize);
        _countdownLayout = l;
        var color = Windows.UI.Color.FromArgb(255, s.DateColor.R, s.DateColor.G, s.DateColor.B);

        double size = l.ArrowSize;
        for (int i = 0; i < ClockDigits.Count; i++)
        {
            var tile = l.Clock.Tiles[i];
            PlaceArrow(_upArrows[i], tile.CenterX, tile.Y - size * 0.75, size, color);
            PlaceArrow(_downArrows[i], tile.CenterX, tile.Bottom + size * 0.75, size, color);
        }

        double band = l.ArrowsFit ? size * 1.5 : 0;
        DescriptionHost.Height = Math.Max(0, 2 * l.DescriptionCenterY);
        DescriptionHost.MaxWidth = Math.Max(0, w - 2 * (TopButtons.Margin.Right + TopButtons.ActualWidth + 8));
        ControlsHost.Margin = new Thickness(0, l.Clock.Tiles[0].Bottom + band, 0, 0);
        ControlsHost.Height = Math.Max(0, h - l.Clock.Tiles[0].Bottom - band);

        // The description reads like the date: same font, weight, color and size.
        var font = new FontFamily(s.DateFontFamily);
        var weight = new FontWeight((ushort)s.DateFontWeight);
        DescriptionText.FontFamily = DescriptionBox.FontFamily = font;
        DescriptionText.FontWeight = DescriptionBox.FontWeight = weight;
        DescriptionText.FontSize = DescriptionBox.FontSize = l.TextFontSize;
        DescriptionBox.MinWidth = l.TextFontSize * 10;
        DescriptionText.Foreground = new SolidColorBrush(color);
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            ((SolidColorBrush)DescriptionBox.Resources[key]).Color = color;
        TileHighlight.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(160, color.R, color.G, color.B));

        RefreshCountdown();
        RequestRender();
    }

    static void PlaceArrow(RepeatButton button, double centerX, double centerY, double size, Windows.UI.Color color)
    {
        button.Width = size * 1.8;
        button.Height = size * 1.3;
        var triangle = (Polygon)button.Content;
        triangle.Width = size;
        triangle.Height = size * 0.6;
        triangle.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(200, color.R, color.G, color.B));
        Canvas.SetLeft(button, centerX - button.Width / 2);
        Canvas.SetTop(button, centerY - button.Height / 2);
    }

    /// <summary>Outlines the tile the keyboard edits; shown only while setting the countdown.</summary>
    void PlaceHighlight()
    {
        if (_selectedTile < 0 || _countdownLayout is null || _countdown is not { State: CountdownState.Idle })
        {
            TileHighlight.Visibility = Visibility.Collapsed;
            return;
        }
        var tile = _countdownLayout.Clock.Tiles[_selectedTile];
        const double Pad = 5;
        TileHighlight.Margin = new Thickness(tile.X - Pad, tile.Y - Pad, 0, 0);
        TileHighlight.Width = tile.Width + 2 * Pad;
        TileHighlight.Height = tile.Height + 2 * Pad;
        TileHighlight.Visibility = Visibility.Visible;
    }

    /// <summary>The tile under a point, arrow bands included, or -1.</summary>
    int TileAt(Windows.Foundation.Point p)
    {
        if (_countdownLayout is not { } l) return -1;
        double band = l.ArrowSize * 1.5;
        for (int i = 0; i < ClockDigits.Count; i++)
        {
            var t = l.Clock.Tiles[i];
            if (p.X >= t.X && p.X <= t.Right && p.Y >= t.Y - band && p.Y <= t.Bottom + band) return i;
        }
        return -1;
    }

    void OnClockWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!_countdownMode || _countdown is not { State: CountdownState.Idle }) return;
        var point = e.GetCurrentPoint(ClockArea);
        int tile = TileAt(point.Position);
        if (tile < 0) return;
        _selectedTile = tile;
        StepTile(tile, Math.Sign(point.Properties.MouseWheelDelta));
        e.Handled = true;
    }

    void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_countdownMode || _countdown is not { State: CountdownState.Idle }) return;
        if (e.OriginalSource is TextBox) return;
        switch (e.Key)
        {
            case VirtualKey.Left:
                _selectedTile = _selectedTile < 0 ? ClockDigits.Count - 1 : Math.Max(0, _selectedTile - 1);
                PlaceHighlight();
                break;
            case VirtualKey.Right:
                _selectedTile = _selectedTile < 0 ? ClockDigits.Count - 1 : Math.Min(ClockDigits.Count - 1, _selectedTile + 1);
                PlaceHighlight();
                break;
            case VirtualKey.Up or VirtualKey.Down:
                if (_selectedTile < 0) _selectedTile = ClockDigits.Count - 1;
                StepTile(_selectedTile, e.Key == VirtualKey.Up ? 1 : -1);
                break;
            default:
                return;
        }
        e.Handled = true;
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

        // Clicking the clock takes focus off the description, so the arrow keys edit the digits.
        if (_countdownMode && DescriptionBox.FocusState != FocusState.Unfocused)
            CountdownButton.Focus(FocusState.Programmatic);

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
        _countdownTimer?.Stop();
        _blinkTimer?.Stop();
        if (_renderingHooked) CompositionTarget.Rendering -= OnRendering;
        _tray?.Dispose();
        _ticks.Dispose();
        _renderer?.Dispose();
        _windowController?.Dispose();
        Application.Current.Exit();
    }
}
