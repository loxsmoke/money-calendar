using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.Tests.Services;

public class EntryQueryServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 19);

    private static Entry Salary(DateOnly start, RecurrenceFrequency frequency, int? dayOfMonth = null) => new()
    {
        Date = start,
        Amount = 2000m,
        Kind = EntryKind.Income,
        CategoryId = DefaultCategories.Salary,
        CurrencyCode = "USD",
        Frequency = frequency,
        DayOfMonth = dayOfMonth,
    };

    [Fact]
    public async Task A_repeating_entry_is_stored_once_and_read_many_times()
    {
        using var db = new TestDatabase(Today);
        await db.Entries.AddAsync(
            Salary(new DateOnly(2026, 8, 1), RecurrenceFrequency.Monthly, dayOfMonth: 15), CancellationToken.None);

        Assert.Equal(1, await db.Entries.CountAsync(CancellationToken.None));

        var occurrences = await db.Queries.GetAsync(
            new EntryFilter(Range: new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31))),
            CancellationToken.None);

        Assert.Equal(3, occurrences.Count);
        Assert.All(occurrences, e => Assert.True(e.IsOccurrence));
        Assert.All(occurrences, e => Assert.Equal(15, e.Date.Day));
        Assert.Equal(2000m, occurrences[0].Amount);
    }

    [Fact]
    public async Task Occurrences_keep_the_template_id_so_edits_reach_the_series()
    {
        using var db = new TestDatabase(Today);
        var template = await db.Entries.AddAsync(
            Salary(new DateOnly(2026, 8, 1), RecurrenceFrequency.Weekly), CancellationToken.None);

        var occurrences = await db.Queries.GetAsync(
            new EntryFilter(Range: new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))),
            CancellationToken.None);

        Assert.All(occurrences, e => Assert.Equal(template.Id, e.Id));
    }

    [Fact]
    public async Task One_off_and_repeating_entries_come_back_together_newest_first()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 20), 45m, EntryKind.Expense, DefaultCategories.Groceries);
        await db.Entries.AddAsync(
            Salary(new DateOnly(2026, 8, 1), RecurrenceFrequency.Monthly, dayOfMonth: 15), CancellationToken.None);

        var rows = await db.Queries.GetAsync(
            new EntryFilter(Range: new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))),
            CancellationToken.None);

        Assert.Equal([new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 15)], rows.Select(r => r.Date));
    }

    [Fact]
    public async Task A_day_filter_returns_the_occurrence_landing_on_that_day()
    {
        using var db = new TestDatabase(Today);
        await db.Entries.AddAsync(
            Salary(new DateOnly(2026, 8, 1), RecurrenceFrequency.Monthly, dayOfMonth: 15), CancellationToken.None);

        var onPayday = await db.Queries.GetAsync(
            new EntryFilter(Day: new DateOnly(2026, 9, 15)), CancellationToken.None);
        var otherDay = await db.Queries.GetAsync(
            new EntryFilter(Day: new DateOnly(2026, 9, 16)), CancellationToken.None);

        Assert.Single(onPayday);
        Assert.Empty(otherDay);
    }

    [Fact]
    public async Task Kind_and_search_filters_still_apply_to_series()
    {
        using var db = new TestDatabase(Today);
        await db.Entries.AddAsync(
            new Entry
            {
                Date = new DateOnly(2026, 8, 1),
                Amount = 1650m,
                Kind = EntryKind.Expense,
                CategoryId = DefaultCategories.Rent,
                CurrencyCode = "USD",
                Note = "Apartment",
                Frequency = RecurrenceFrequency.Monthly,
                DayOfMonth = 1,
            },
            CancellationToken.None);
        await db.Entries.AddAsync(
            Salary(new DateOnly(2026, 8, 1), RecurrenceFrequency.Monthly, dayOfMonth: 15), CancellationToken.None);

        var range = new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30));
        var expenses = await db.Queries.GetAsync(new EntryFilter(Range: range, Kind: EntryKind.Expense), CancellationToken.None);
        var apartment = await db.Queries.GetAsync(new EntryFilter(Range: range, Search: "Apartment"), CancellationToken.None);

        Assert.Equal(2, expenses.Count);
        Assert.All(expenses, e => Assert.Equal(EntryKind.Expense, e.Kind));
        Assert.Equal(2, apartment.Count);
    }

    [Fact]
    public async Task Summaries_see_the_occurrences()
    {
        using var db = new TestDatabase(Today);
        await db.Entries.AddAsync(
            Salary(new DateOnly(2026, 8, 1), RecurrenceFrequency.TwiceMonthly, dayOfMonth: 1), CancellationToken.None);

        var summary = await db.Summaries.GetRangeSummaryAsync(
            DateRange.Month(2026, 8), CancellationToken.None);

        // Twice monthly with no second day set falls back to the same day, so it lands once.
        Assert.Equal(2000m, summary.TotalIncome);
        Assert.Equal(2000m, summary.Days.Single(d => d.Date == new DateOnly(2026, 8, 1)).Income);
    }

    [Fact]
    public async Task Without_a_range_only_stored_rows_come_back()
    {
        using var db = new TestDatabase(Today);
        await db.Entries.AddAsync(
            Salary(new DateOnly(2026, 8, 1), RecurrenceFrequency.Weekly), CancellationToken.None);

        var stored = await db.Queries.GetAsync(new EntryFilter(), CancellationToken.None);

        // Nothing to expand into: a series has no end, so an unbounded read stays at the template.
        Assert.Single(stored);
        Assert.False(stored[0].IsOccurrence);
    }
}
