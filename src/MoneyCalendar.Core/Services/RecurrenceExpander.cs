using System.Globalization;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Services;

/// <summary>
/// Turns a repeating entry's pattern into the dates it lands on inside a range, and describes
/// that pattern in words for the UI. Nothing here touches storage: a series is one stored row
/// plus the dates this produces.
/// </summary>
public static class RecurrenceExpander
{
    /// <summary>
    /// Every occurrence of <paramref name="template"/> inside <paramref name="range"/>, in date
    /// order. A series never starts before the template's own date, so the range's start is
    /// clamped forward to it. A one-off entry yields its own date when the range contains it.
    /// </summary>
    public static IReadOnlyList<DateOnly> Occurrences(Entry template, DateRange range)
    {
        if (!template.IsRecurring)
            return range.Contains(template.Date) ? [template.Date] : [];

        var from = template.Date > range.From ? template.Date : range.From;
        // A series that ends stops there, however far the range reaches.
        var to = template.RecurrenceEnd is { } end && end < range.To ? end : range.To;
        if (from > to)
            return [];

        return template.Frequency switch
        {
            RecurrenceFrequency.Weekly => EveryNDays(template, from, to, 7),
            RecurrenceFrequency.BiWeekly => EveryNDays(template, from, to, 14),
            RecurrenceFrequency.Monthly => MonthlyDays(template, from, to),
            RecurrenceFrequency.TwiceMonthly => TwiceMonthlyDays(template, from, to),
            _ => [],
        };
    }

    /// <summary>
    /// Weekly and bi-weekly. The rhythm is anchored on the template's start date so a
    /// bi-weekly series keeps landing on the same alternate weeks however the range moves.
    /// </summary>
    private static List<DateOnly> EveryNDays(Entry template, DateOnly from, DateOnly to, int step)
    {
        var anchor = template.Date;
        if (template.Weekday is { } weekday)
        {
            var shift = ((int)weekday - (int)anchor.DayOfWeek + 7) % 7;
            anchor = anchor.AddDays(shift);
        }

        if (anchor > to)
            return [];

        // Jump straight to the first occurrence at or after the window start.
        if (anchor < from)
        {
            var elapsed = from.DayNumber - anchor.DayNumber;
            var skips = (elapsed + step - 1) / step;
            anchor = anchor.AddDays(skips * step);
        }

        var dates = new List<DateOnly>();
        for (var date = anchor; date <= to; date = date.AddDays(step))
            dates.Add(date);
        return dates;
    }

    private static List<DateOnly> MonthlyDays(Entry template, DateOnly from, DateOnly to)
    {
        var day = template.DayOfMonth ?? template.Date.Day;
        var dates = new List<DateOnly>();

        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var date = InMonth(month, day, MonthDayMode.OnDay);
            if (date >= from && date <= to && date >= template.Date)
                dates.Add(date);
        }

        return dates;
    }

    private static List<DateOnly> TwiceMonthlyDays(Entry template, DateOnly from, DateOnly to)
    {
        var first = template.DayOfMonth ?? template.Date.Day;
        var dates = new List<DateOnly>();

        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var candidates = new[]
            {
                InMonth(month, first, MonthDayMode.OnDay),
                InMonth(month, template.SecondDayOfMonth ?? first, template.SecondDayMode),
            };

            foreach (var date in candidates.Distinct().OrderBy(d => d))
            {
                if (date >= from && date <= to && date >= template.Date)
                    dates.Add(date);
            }
        }

        return dates;
    }

    /// <summary>
    /// Resolves a day-of-month rule inside one month. A day past the end of a short month
    /// lands on its last day, so "the 31st" is the 28th in February rather than skipped.
    /// </summary>
    private static DateOnly InMonth(DateOnly month, int day, MonthDayMode mode)
    {
        var length = DateTime.DaysInMonth(month.Year, month.Month);
        var resolved = mode switch
        {
            MonthDayMode.MidMonth => 15,
            MonthDayMode.LastDay => length,
            _ => Math.Clamp(day, 1, length),
        };
        return new DateOnly(month.Year, month.Month, resolved);
    }

    /// <summary>A short human description, e.g. "Twice monthly on the 1st and the last day".</summary>
    public static string Describe(Entry template)
    {
        if (!template.IsRecurring)
            return "One-off";

        var culture = CultureInfo.CurrentCulture;
        var ending = template.RecurrenceEnd is { } end
            ? $", until {end.ToString("MMM d, yyyy", culture)}"
            : "";
        return Pattern(template, culture) + ending;
    }

    private static string Pattern(Entry template, CultureInfo culture)
    {
        return template.Frequency switch
        {
            RecurrenceFrequency.Weekly =>
                $"Weekly on {culture.DateTimeFormat.GetDayName(template.Weekday ?? template.Date.DayOfWeek)}",
            RecurrenceFrequency.BiWeekly =>
                $"Every 2 weeks on {culture.DateTimeFormat.GetDayName(template.Weekday ?? template.Date.DayOfWeek)}",
            RecurrenceFrequency.Monthly =>
                $"Monthly on the {Ordinal(template.DayOfMonth ?? template.Date.Day)}",
            RecurrenceFrequency.TwiceMonthly =>
                $"Twice monthly on the {Ordinal(template.DayOfMonth ?? template.Date.Day)} and " +
                $"{SecondDayText(template)}",
            _ => "One-off",
        };
    }

    private static string SecondDayText(Entry template) => template.SecondDayMode switch
    {
        MonthDayMode.MidMonth => "mid month",
        MonthDayMode.LastDay => "the last day",
        _ => $"the {Ordinal(template.SecondDayOfMonth ?? template.DayOfMonth ?? template.Date.Day)}",
    };

    /// <summary>"1st", "2nd", "23rd" — English ordinals, which is what the UI text is written in.</summary>
    public static string Ordinal(int day)
    {
        var suffix = (day % 100) is >= 11 and <= 13
            ? "th"
            : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return day.ToString(CultureInfo.CurrentCulture) + suffix;
    }
}
