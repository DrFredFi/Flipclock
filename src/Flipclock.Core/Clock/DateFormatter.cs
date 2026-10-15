using System.Globalization;
using Flipclock.Core.Settings;

namespace Flipclock.Core.Clock;

/// <summary>Formats the date line as "Weekday, D Month YYYY", e.g. "Giovedì, 15 Ottobre 2026".</summary>
public static class DateFormatter
{
    public static CultureInfo CultureFor(DateLanguage language) => CultureInfo.GetCultureInfo(language switch
    {
        DateLanguage.English => "en-GB",
        DateLanguage.Spanish => "es-ES",
        _ => "it-IT",
    });

    public static string Format(DateTime date, DateLanguage language)
    {
        var culture = CultureFor(language);
        var dtf = culture.DateTimeFormat;
        var day = Capitalize(dtf.GetDayName(date.DayOfWeek), culture);
        var month = Capitalize(dtf.GetMonthName(date.Month), culture);
        return $"{day}, {date.Day} {month} {date.Year}";
    }

    static string Capitalize(string s, CultureInfo culture) =>
        s.Length == 0 ? s : char.ToUpper(s[0], culture) + s[1..];
}
