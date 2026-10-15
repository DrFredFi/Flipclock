using Flipclock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;
using Color = Windows.UI.Color;

namespace Flipclock.App;

[Flags]
public enum SettingsChange
{
    None = 0,
    Layout = 1,
    Appearance = 2,
    DigitFont = 4,
    BackgroundImage = 8,
    Sound = 16,
    Date = 32,
}

public sealed partial class SettingsPanel : UserControl
{
    static readonly (int Weight, Func<Strings, string> Name)[] Weights =
    [
        (300, l => l.WeightLight), (400, l => l.WeightRegular), (500, l => l.WeightMedium),
        (600, l => l.WeightSemiBold), (700, l => l.WeightBold), (900, l => l.WeightBlack),
    ];

    ClockSettings _settings = new();
    bool _loading;
    readonly Dictionary<string, (DropDownButton Button, ColorPicker Picker, Rectangle Swatch)> _colors = [];

    public SettingsPanel()
    {
        InitializeComponent();
        foreach (var (w, name) in Weights)
        {
            DigitWeightBox.Items.Add(name(L));
            DateWeightBox.Items.Add(name(L));
        }
        foreach (var p in new[] { L.Left, L.Center, L.Right }) DatePositionBox.Items.Add(p);
        foreach (var p in new[] { L.LanguageItalian, L.LanguageEnglish, L.LanguageSpanish }) DateLanguageBox.Items.Add(p);

        SetupColor(TileColorButton, L.TileColor);
        SetupColor(DigitColorButton, L.DigitColor);
        SetupColor(DateColorButton, L.DateColor);
        SetupColor(BackgroundColorButton, L.BackgroundColor);
    }

    public Strings L => Strings.Current;

    /// <summary>The window handle that owns file pickers.</summary>
    public nint OwnerHwnd { get; set; }

    public event Action<SettingsChange>? Changed;
    public event Action? CloseRequested;
    public event Action? TestFlipRequested;
    public event Action? TestTickRequested;
    public event Action? ResetRequested;
    public event Action<bool>? AlwaysOnTopChanged;
    public event Action<bool>? BorderlessChanged;
    public event Action<bool>? FullscreenChanged;

    public void SetFonts(IReadOnlyList<string> families)
    {
        DigitFontBox.ItemsSource = families;
        DateFontBox.ItemsSource = families;
    }

    /// <summary>Shows the given settings; edits are written straight back into the same instance.</summary>
    public void Load(ClockSettings settings)
    {
        _settings = settings;
        _loading = true;
        try
        {
            SecondsScaleSlider.Value = settings.SecondsScale * 100;
            MarginSlider.Value = settings.Margin;
            SetColor("tile", settings.TileColor);
            SetColor("digit", settings.DigitColor);
            SetColor("date", settings.DateColor);
            SetColor("background", settings.BackgroundColor);
            SelectFont(DigitFontBox, settings.DigitFontFamily);
            SelectFont(DateFontBox, settings.DateFontFamily);
            DigitWeightBox.SelectedIndex = WeightIndex(settings.DigitFontWeight);
            DateWeightBox.SelectedIndex = WeightIndex(settings.DateFontWeight);
            LightAngleSlider.Value = settings.LightAngle;
            LightIntensitySlider.Value = settings.LightIntensity * 100;
            GlossinessSlider.Value = settings.Glossiness * 100;
            FlipDurationSlider.Value = settings.FlipDurationMs;
            CascadeDelaySlider.Value = settings.CascadeDelayMs;
            BounceSlider.Value = settings.Bounce * 100;
            SoundEnabledSwitch.IsOn = settings.SoundEnabled;
            VolumeSlider.Value = settings.Volume * 100;
            SoundFileName.Text = DisplayName(settings.SoundFile, L.DefaultSound);
            BackgroundFileName.Text = DisplayName(settings.BackgroundImage, L.NoImage);
            BackgroundDimSlider.Value = settings.BackgroundDim * 100;
            DateVisibleSwitch.IsOn = settings.DateVisible;
            DatePositionBox.SelectedIndex = (int)settings.DatePosition;
            DateSizeSlider.Value = settings.DateSize * 100;
            DateLanguageBox.SelectedIndex = (int)settings.DateLanguage;
        }
        finally
        {
            _loading = false;
        }
    }

    public void SetWindowState(bool alwaysOnTop, bool borderless, bool fullscreen)
    {
        _loading = true;
        AlwaysOnTopSwitch.IsOn = alwaysOnTop;
        BorderlessSwitch.IsOn = borderless;
        FullscreenSwitch.IsOn = fullscreen;
        _loading = false;
    }

    static string DisplayName(string path, string fallback) =>
        string.IsNullOrWhiteSpace(path) ? fallback : System.IO.Path.GetFileName(path);

    static int WeightIndex(int weight)
    {
        int best = 0;
        for (int i = 1; i < Weights.Length; i++)
            if (Math.Abs(Weights[i].Weight - weight) < Math.Abs(Weights[best].Weight - weight)) best = i;
        return best;
    }

    static void SelectFont(ComboBox box, string family)
    {
        if (box.ItemsSource is not IReadOnlyList<string> list) return;
        int index = -1;
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i], family, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
        box.SelectedIndex = index;
    }

    void SetupColor(DropDownButton button, string label)
    {
        var swatch = new Rectangle { Width = 18, Height = 18, RadiusX = 3, RadiusY = 3, Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)) };
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { swatch, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
        };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
        };
        var key = (string)button.Tag;
        picker.ColorChanged += (_, e) =>
        {
            swatch.Fill = new SolidColorBrush(e.NewColor);
            if (_loading) return;
            var c = new RgbColor(e.NewColor.R, e.NewColor.G, e.NewColor.B);
            switch (key)
            {
                case "tile": _settings.TileColor = c; break;
                case "digit": _settings.DigitColor = c; break;
                case "date": _settings.DateColor = c; Changed?.Invoke(SettingsChange.Date); return;
                case "background": _settings.BackgroundColor = c; break;
            }
            Changed?.Invoke(SettingsChange.Appearance);
        };
        button.Flyout = new Flyout { Content = picker, Placement = FlyoutPlacementMode.Left };
        _colors[key] = (button, picker, swatch);
    }

    void SetColor(string key, RgbColor c)
    {
        var (_, picker, swatch) = _colors[key];
        var color = Color.FromArgb(255, c.R, c.G, c.B);
        picker.Color = color;
        swatch.Fill = new SolidColorBrush(color);
    }

    void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        var s = _settings;
        var change = sender switch
        {
            _ when sender == SecondsScaleSlider => Set(() => s.SecondsScale = e.NewValue / 100, SettingsChange.Layout),
            _ when sender == MarginSlider => Set(() => s.Margin = e.NewValue, SettingsChange.Layout),
            _ when sender == LightAngleSlider => Set(() => s.LightAngle = e.NewValue, SettingsChange.Appearance),
            _ when sender == LightIntensitySlider => Set(() => s.LightIntensity = e.NewValue / 100, SettingsChange.Appearance),
            _ when sender == GlossinessSlider => Set(() => s.Glossiness = e.NewValue / 100, SettingsChange.Appearance),
            _ when sender == FlipDurationSlider => Set(() => s.FlipDurationMs = (int)e.NewValue, SettingsChange.None),
            _ when sender == CascadeDelaySlider => Set(() => s.CascadeDelayMs = (int)e.NewValue, SettingsChange.None),
            _ when sender == BounceSlider => Set(() => s.Bounce = e.NewValue / 100, SettingsChange.None),
            _ when sender == VolumeSlider => Set(() => s.Volume = e.NewValue / 100, SettingsChange.None),
            _ when sender == BackgroundDimSlider => Set(() => s.BackgroundDim = e.NewValue / 100, SettingsChange.Appearance),
            _ when sender == DateSizeSlider => Set(() => s.DateSize = e.NewValue / 100, SettingsChange.Date),
            _ => SettingsChange.None,
        };
        Changed?.Invoke(change);

        static SettingsChange Set(Action apply, SettingsChange change)
        {
            apply();
            return change;
        }
    }

    void OnComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not ComboBox box || box.SelectedIndex < 0) return;
        var s = _settings;
        SettingsChange change;
        if (box == DigitFontBox) { s.DigitFontFamily = (string)box.SelectedItem; change = SettingsChange.DigitFont; }
        else if (box == DigitWeightBox) { s.DigitFontWeight = Weights[box.SelectedIndex].Weight; change = SettingsChange.DigitFont; }
        else if (box == DateFontBox) { s.DateFontFamily = (string)box.SelectedItem; change = SettingsChange.Date; }
        else if (box == DateWeightBox) { s.DateFontWeight = Weights[box.SelectedIndex].Weight; change = SettingsChange.Date; }
        else if (box == DatePositionBox) { s.DatePosition = (DatePosition)box.SelectedIndex; change = SettingsChange.Date; }
        else if (box == DateLanguageBox) { s.DateLanguage = (DateLanguage)box.SelectedIndex; change = SettingsChange.Date; }
        else return;
        Changed?.Invoke(change);
    }

    void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender == SoundEnabledSwitch) { _settings.SoundEnabled = SoundEnabledSwitch.IsOn; Changed?.Invoke(SettingsChange.None); }
        else if (sender == DateVisibleSwitch) { _settings.DateVisible = DateVisibleSwitch.IsOn; Changed?.Invoke(SettingsChange.Date); }
        else if (sender == AlwaysOnTopSwitch) AlwaysOnTopChanged?.Invoke(AlwaysOnTopSwitch.IsOn);
        else if (sender == BorderlessSwitch) BorderlessChanged?.Invoke(BorderlessSwitch.IsOn);
        else if (sender == FullscreenSwitch) FullscreenChanged?.Invoke(FullscreenSwitch.IsOn);
    }

    async Task<string?> PickFile(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    async void OnBrowseSoundClick(object sender, RoutedEventArgs e)
    {
        var path = await PickFile(".mp3");
        if (path is null) return;
        _settings.SoundFile = path;
        SoundFileName.Text = DisplayName(path, L.DefaultSound);
        Changed?.Invoke(SettingsChange.Sound);
    }

    void OnClearSoundClick(object sender, RoutedEventArgs e)
    {
        _settings.SoundFile = "";
        SoundFileName.Text = L.DefaultSound;
        Changed?.Invoke(SettingsChange.Sound);
    }

    async void OnBrowseImageClick(object sender, RoutedEventArgs e)
    {
        var path = await PickFile(".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif");
        if (path is null) return;
        _settings.BackgroundImage = path;
        BackgroundFileName.Text = DisplayName(path, L.NoImage);
        Changed?.Invoke(SettingsChange.BackgroundImage);
    }

    void OnClearImageClick(object sender, RoutedEventArgs e)
    {
        _settings.BackgroundImage = "";
        BackgroundFileName.Text = L.NoImage;
        Changed?.Invoke(SettingsChange.BackgroundImage);
    }

    void OnTestFlipClick(object sender, RoutedEventArgs e) => TestFlipRequested?.Invoke();
    void OnTestTickClick(object sender, RoutedEventArgs e) => TestTickRequested?.Invoke();
    void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    async void OnResetClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = L.ResetConfirmTitle,
            Content = L.ResetConfirmBody,
            PrimaryButtonText = L.Reset,
            CloseButtonText = L.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ResetRequested?.Invoke();
    }
}
