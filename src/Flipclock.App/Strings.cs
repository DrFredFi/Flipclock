using System.Globalization;

namespace Flipclock.App;

/// <summary>UI text in the Windows display language: Italian when it is Italian, English otherwise.</summary>
public sealed class Strings
{
    Strings() { }

    public static Strings Current { get; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? Italian() : English();

    public required string Settings { get; init; }
    public required string Close { get; init; }

    public required string SectionClock { get; init; }
    public required string SecondsSize { get; init; }
    public required string Margin { get; init; }

    public required string SectionColors { get; init; }
    public required string TileColor { get; init; }
    public required string DigitColor { get; init; }
    public required string DateColor { get; init; }
    public required string BackgroundColor { get; init; }

    public required string SectionFonts { get; init; }
    public required string DigitFont { get; init; }
    public required string DateFont { get; init; }
    public required string Weight { get; init; }

    public required string SectionLighting { get; init; }
    public required string LightAngle { get; init; }
    public required string LightIntensity { get; init; }
    public required string Finish { get; init; }

    public required string SectionAnimation { get; init; }
    public required string FlipDuration { get; init; }
    public required string CascadeDelay { get; init; }
    public required string Bounce { get; init; }
    public required string TestFlip { get; init; }

    public required string SectionSound { get; init; }
    public required string TickSound { get; init; }
    public required string Volume { get; init; }
    public required string SoundFile { get; init; }
    public required string DefaultSound { get; init; }
    public required string TestTick { get; init; }

    public required string SectionBackground { get; init; }
    public required string BackgroundImage { get; init; }
    public required string NoImage { get; init; }
    public required string Dim { get; init; }

    public required string SectionDate { get; init; }
    public required string ShowDate { get; init; }
    public required string Position { get; init; }
    public required string Left { get; init; }
    public required string Center { get; init; }
    public required string Right { get; init; }
    public required string DateSize { get; init; }
    public required string DateLanguage { get; init; }
    public required string LanguageItalian { get; init; }
    public required string LanguageEnglish { get; init; }
    public required string LanguageSpanish { get; init; }

    public required string SectionWindow { get; init; }
    public required string AlwaysOnTop { get; init; }
    public required string Borderless { get; init; }
    public required string Fullscreen { get; init; }

    public required string Browse { get; init; }
    public required string Clear { get; init; }
    public required string ResetDefaults { get; init; }
    public required string ResetConfirmTitle { get; init; }
    public required string ResetConfirmBody { get; init; }
    public required string Reset { get; init; }
    public required string Cancel { get; init; }

    public required string Countdown { get; init; }
    public required string BackToClock { get; init; }
    public required string Start { get; init; }
    public required string Pause { get; init; }
    public required string Continue { get; init; }
    public required string CountdownDescription { get; init; }
    public required string DigitUp { get; init; }
    public required string DigitDown { get; init; }
    public required string CountdownFinished { get; init; }

    public required string TrayShowHide { get; init; }
    public required string TrayMuteTick { get; init; }
    public required string TrayExit { get; init; }

    public required string ConfigNotWritableTitle { get; init; }
    public required string ConfigNotWritableBody { get; init; }
    public required string ConfigCorruptBody { get; init; }

    public required string WeightLight { get; init; }
    public required string WeightRegular { get; init; }
    public required string WeightMedium { get; init; }
    public required string WeightSemiBold { get; init; }
    public required string WeightBold { get; init; }
    public required string WeightBlack { get; init; }

    public required string Matte { get; init; }
    public required string Glossy { get; init; }

    static Strings English() => new()
    {
        Settings = "Settings", Close = "Close",
        SectionClock = "Clock", SecondsSize = "Seconds tile size", Margin = "Margin",
        SectionColors = "Colors", TileColor = "Tiles", DigitColor = "Digits", DateColor = "Date", BackgroundColor = "Background",
        SectionFonts = "Fonts", DigitFont = "Digit font", DateFont = "Date font", Weight = "Weight",
        SectionLighting = "Lighting", LightAngle = "Light angle", LightIntensity = "Light intensity", Finish = "Finish",
        SectionAnimation = "Animation", FlipDuration = "Flip duration", CascadeDelay = "Cascade delay", Bounce = "Bounce", TestFlip = "Test flip",
        SectionSound = "Sound", TickSound = "Tick sound", Volume = "Volume", SoundFile = "Sound file", DefaultSound = "Default tick", TestTick = "Test tick",
        SectionBackground = "Background", BackgroundImage = "Image", NoImage = "No image", Dim = "Dim",
        SectionDate = "Date", ShowDate = "Show date", Position = "Position", Left = "Left", Center = "Center", Right = "Right",
        DateSize = "Size", DateLanguage = "Language", LanguageItalian = "Italiano", LanguageEnglish = "English", LanguageSpanish = "Español",
        SectionWindow = "Window", AlwaysOnTop = "Always on top", Borderless = "Borderless (Ctrl+B)", Fullscreen = "Fullscreen (F11)",
        Browse = "Browse…", Clear = "Clear", ResetDefaults = "Reset to defaults",
        ResetConfirmTitle = "Reset to defaults?", ResetConfirmBody = "All appearance, sound and animation settings return to their defaults.",
        Reset = "Reset", Cancel = "Cancel",
        Countdown = "Countdown", BackToClock = "Clock", Start = "Start", Pause = "Pause", Continue = "Continue",
        CountdownDescription = "Description…", DigitUp = "Increase", DigitDown = "Decrease", CountdownFinished = "Countdown finished",
        TrayShowHide = "Show / Hide", TrayMuteTick = "Mute tick", TrayExit = "Exit",
        ConfigNotWritableTitle = "Settings will not be saved",
        ConfigNotWritableBody = "The folder containing Flipclock is not writable. Move the app to a writable folder to keep your settings.",
        ConfigCorruptBody = "config.toml could not be read and was saved as config.toml.bak. Defaults are in use.",
        WeightLight = "Light", WeightRegular = "Regular", WeightMedium = "Medium", WeightSemiBold = "Semibold", WeightBold = "Bold", WeightBlack = "Black",
        Matte = "Matte", Glossy = "Glossy",
    };

    static Strings Italian() => new()
    {
        Settings = "Impostazioni", Close = "Chiudi",
        SectionClock = "Orologio", SecondsSize = "Dimensione secondi", Margin = "Margine",
        SectionColors = "Colori", TileColor = "Tessere", DigitColor = "Cifre", DateColor = "Data", BackgroundColor = "Sfondo",
        SectionFonts = "Caratteri", DigitFont = "Carattere cifre", DateFont = "Carattere data", Weight = "Spessore",
        SectionLighting = "Illuminazione", LightAngle = "Angolo luce", LightIntensity = "Intensità luce", Finish = "Finitura",
        SectionAnimation = "Animazione", FlipDuration = "Durata ribaltamento", CascadeDelay = "Ritardo a cascata", Bounce = "Rimbalzo", TestFlip = "Prova animazione",
        SectionSound = "Suono", TickSound = "Suono tic", Volume = "Volume", SoundFile = "File audio", DefaultSound = "Tic predefinito", TestTick = "Prova suono",
        SectionBackground = "Sfondo", BackgroundImage = "Immagine", NoImage = "Nessuna immagine", Dim = "Oscuramento",
        SectionDate = "Data", ShowDate = "Mostra data", Position = "Posizione", Left = "Sinistra", Center = "Centro", Right = "Destra",
        DateSize = "Dimensione", DateLanguage = "Lingua", LanguageItalian = "Italiano", LanguageEnglish = "English", LanguageSpanish = "Español",
        SectionWindow = "Finestra", AlwaysOnTop = "Sempre in primo piano", Borderless = "Senza bordi (Ctrl+B)", Fullscreen = "Schermo intero (F11)",
        Browse = "Sfoglia…", Clear = "Rimuovi", ResetDefaults = "Ripristina predefiniti",
        ResetConfirmTitle = "Ripristinare i predefiniti?", ResetConfirmBody = "Tutte le impostazioni di aspetto, suono e animazione tornano ai valori predefiniti.",
        Reset = "Ripristina", Cancel = "Annulla",
        Countdown = "Conto alla rovescia", BackToClock = "Orologio", Start = "Avvia", Pause = "Pausa", Continue = "Continua",
        CountdownDescription = "Descrizione…", DigitUp = "Aumenta", DigitDown = "Diminuisci", CountdownFinished = "Conto alla rovescia terminato",
        TrayShowHide = "Mostra / Nascondi", TrayMuteTick = "Silenzia tic", TrayExit = "Esci",
        ConfigNotWritableTitle = "Le impostazioni non verranno salvate",
        ConfigNotWritableBody = "La cartella di Flipclock non è scrivibile. Sposta l'applicazione in una cartella scrivibile per conservare le impostazioni.",
        ConfigCorruptBody = "Impossibile leggere config.toml: è stato salvato come config.toml.bak. Sono in uso i valori predefiniti.",
        WeightLight = "Leggero", WeightRegular = "Normale", WeightMedium = "Medio", WeightSemiBold = "Semigrassetto", WeightBold = "Grassetto", WeightBlack = "Nero",
        Matte = "Opaco", Glossy = "Lucido",
    };
}
