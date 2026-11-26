using System;
using System.Globalization;

namespace C4TChat.Services;

internal enum PlayerDateKind
{
    Birthday,
    Anniversary,
}

internal readonly record struct PlayerDate(int Month, int Day, int? Year)
{
    internal static bool TryParse(string? text, out PlayerDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('-');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var d)
            && IsValid(m, d))
        {
            date = new PlayerDate(m, d, null);
            return true;
        }

        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out m)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out d)
            && IsValid(m, d) && y is >= 1900 and <= 2200)
        {
            date = new PlayerDate(m, d, y);
            return true;
        }

        return false;
    }

    internal static bool IsValid(int month, int day) => month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2024, month);

    internal string Serialize() => Year is { } y
        ? string.Create(CultureInfo.InvariantCulture, $"{y:0000}-{Month:00}-{Day:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{Month:00}-{Day:00}");

    internal DateTime NextOccurrence(DateTime today)
    {
        today = today.Date;
        var candidate = On(today.Year);
        return candidate >= today ? candidate : On(today.Year + 1);
    }

    internal int DaysUntil(DateTime today) => (int)(NextOccurrence(today) - today.Date).TotalDays;

    internal int? YearsAt(DateTime occurrence) => Year is { } y && occurrence.Year > y ? occurrence.Year - y : null;

    internal string Display()
    {
        var culture = Loc.Culture;
        var german = culture.Name == "de-DE";
        var sample = new DateTime(Year ?? 2024, Month, Day);
        return Year != null
            ? sample.ToString(german ? "d. MMMM yyyy" : "MMMM d, yyyy", culture)
            : sample.ToString(german ? "d. MMMM" : "MMMM d", culture);
    }

    private DateTime On(int year)
    {
        var day = Month == 2 && Day == 29 && !DateTime.IsLeapYear(year) ? 28 : Day;
        return new DateTime(year, Month, day);
    }
}
