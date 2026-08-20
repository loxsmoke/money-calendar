using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;
using MoneyCalendar.Core.Services;

namespace MoneyCalendar.Tests.Services;

public class RecurrenceExpanderTests
{
    private static Entry Template(
        DateOnly start,
        RecurrenceFrequency frequency,
        int? dayOfMonth = null,
        int? secondDay = null,
        MonthDayMode secondMode = MonthDayMode.OnDay,
        DayOfWeek? weekday = null,
        DateOnly? endsAfter = null) =>
        new()
        {
            Date = start,
            Amount = 100m,
            Kind = EntryKind.Income,
            CategoryId = DefaultCategories.Salary,
            CurrencyCode = "USD",
            Frequency = frequency,
            DayOfMonth = dayOfMonth,
            SecondDayOfMonth = secondDay,
            SecondDayMode = secondMode,
            Weekday = weekday,
            RecurrenceEnd = endsAfter,
        };

    private static DateRange Range(DateOnly from, DateOnly to) => new(from, to);

    [Fact]
    public void A_one_off_entry_yields_its_own_date_when_the_range_covers_it()
    {
        var entry = Template(new DateOnly(2026, 8, 10), RecurrenceFrequency.None);

        Assert.Equal(
            [new DateOnly(2026, 8, 10)],
            RecurrenceExpander.Occurrences(entry, Range(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))));
        Assert.Empty(
            RecurrenceExpander.Occurrences(entry, Range(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))));
    }

    [Fact]
    public void Weekly_lands_on_the_chosen_weekday()
    {
        // 2026-08-03 is a Monday; the series is asked to land on Fridays.
        var entry = Template(new DateOnly(2026, 8, 3), RecurrenceFrequency.Weekly, weekday: DayOfWeek.Friday);

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));

        Assert.Equal(
            [new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 21), new DateOnly(2026, 8, 28)],
            dates);
        Assert.All(dates, d => Assert.Equal(DayOfWeek.Friday, d.DayOfWeek));
    }

    [Fact]
    public void Bi_weekly_keeps_its_rhythm_when_the_window_moves()
    {
        var entry = Template(new DateOnly(2026, 8, 7), RecurrenceFrequency.BiWeekly, weekday: DayOfWeek.Friday);

        var august = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        var september = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));

        Assert.Equal([new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 21)], august);
        // The alternate weeks continue across the window boundary rather than restarting.
        Assert.Equal([new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 18)], september);
    }

    [Fact]
    public void Monthly_lands_on_the_chosen_day()
    {
        var entry = Template(new DateOnly(2026, 1, 1), RecurrenceFrequency.Monthly, dayOfMonth: 15);

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 31)));

        Assert.Equal(
            [new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15), new DateOnly(2026, 5, 15)],
            dates);
    }

    [Fact]
    public void A_day_past_the_end_of_a_short_month_lands_on_its_last_day()
    {
        var entry = Template(new DateOnly(2026, 1, 1), RecurrenceFrequency.Monthly, dayOfMonth: 31);

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30)));

        Assert.Equal(
            [
                new DateOnly(2026, 1, 31),
                new DateOnly(2026, 2, 28),
                new DateOnly(2026, 3, 31),
                new DateOnly(2026, 4, 30),
            ],
            dates);
    }

    [Fact]
    public void Twice_monthly_lands_on_both_chosen_days()
    {
        var entry = Template(new DateOnly(2026, 8, 1), RecurrenceFrequency.TwiceMonthly,
            dayOfMonth: 1, secondDay: 16);

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30)));

        Assert.Equal(
            [
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 16),
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 16),
            ],
            dates);
    }

    [Fact]
    public void Twice_monthly_mid_month_resolves_to_the_15th()
    {
        var entry = Template(new DateOnly(2026, 8, 1), RecurrenceFrequency.TwiceMonthly,
            dayOfMonth: 1, secondMode: MonthDayMode.MidMonth);

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));

        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15)], dates);
    }

    [Fact]
    public void Twice_monthly_last_day_follows_the_month_length()
    {
        var entry = Template(new DateOnly(2026, 1, 1), RecurrenceFrequency.TwiceMonthly,
            dayOfMonth: 15, secondMode: MonthDayMode.LastDay);

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28)));

        Assert.Equal(
            [
                new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31),
                new DateOnly(2026, 2, 15), new DateOnly(2026, 2, 28),
            ],
            dates);
    }

    [Fact]
    public void A_series_never_starts_before_its_start_date()
    {
        var entry = Template(new DateOnly(2026, 8, 20), RecurrenceFrequency.Monthly, dayOfMonth: 1);

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 6, 1), new DateOnly(2026, 10, 31)));

        // August's 1st is before the series begins, so the first occurrence is September's.
        Assert.Equal([new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], dates);
    }

    [Fact]
    public void A_series_stops_on_its_end_date()
    {
        var entry = Template(
            new DateOnly(2026, 1, 1), RecurrenceFrequency.Monthly, dayOfMonth: 10,
            endsAfter: new DateOnly(2026, 3, 31));

        var dates = RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

        Assert.Equal(
            [new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 10), new DateOnly(2026, 3, 10)],
            dates);
    }

    [Fact]
    public void An_end_date_before_the_window_yields_nothing()
    {
        var entry = Template(
            new DateOnly(2026, 1, 1), RecurrenceFrequency.Weekly,
            weekday: DayOfWeek.Monday, endsAfter: new DateOnly(2026, 2, 1));

        Assert.Empty(RecurrenceExpander.Occurrences(
            entry, Range(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))));
    }

    [Fact]
    public void An_end_date_lands_in_the_description()
    {
        var entry = Template(
            new DateOnly(2026, 1, 1), RecurrenceFrequency.Monthly, dayOfMonth: 3,
            endsAfter: new DateOnly(2026, 6, 30));

        Assert.Equal("Monthly on the 3rd, until Jun 30, 2026", RecurrenceExpander.Describe(entry));
    }

    [Fact]
    public void Descriptions_read_as_sentences()
    {
        Assert.Equal(
            "Monthly on the 3rd",
            RecurrenceExpander.Describe(Template(new DateOnly(2026, 8, 1), RecurrenceFrequency.Monthly, dayOfMonth: 3)));
        Assert.Equal(
            "Twice monthly on the 1st and the last day",
            RecurrenceExpander.Describe(Template(
                new DateOnly(2026, 8, 1), RecurrenceFrequency.TwiceMonthly,
                dayOfMonth: 1, secondMode: MonthDayMode.LastDay)));
        Assert.Equal(
            "Every 2 weeks on Friday",
            RecurrenceExpander.Describe(Template(
                new DateOnly(2026, 8, 7), RecurrenceFrequency.BiWeekly, weekday: DayOfWeek.Friday)));
        Assert.Equal("One-off", RecurrenceExpander.Describe(Template(new DateOnly(2026, 8, 7), RecurrenceFrequency.None)));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(31, "31st")]
    public void Ordinals_use_english_suffixes(int day, string expected) =>
        Assert.Equal(expected, RecurrenceExpander.Ordinal(day));
}
