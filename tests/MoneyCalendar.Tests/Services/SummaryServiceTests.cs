using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.Tests.Services;

public class SummaryServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 18);

    [Fact]
    public async Task Range_summary_totals_income_and_expenses_separately()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 3), 2000m, EntryKind.Income, DefaultCategories.Salary);
        await db.AddAsync(new DateOnly(2026, 8, 4), 1650m, EntryKind.Expense, DefaultCategories.Rent);
        await db.AddAsync(new DateOnly(2026, 8, 4), 42.50m, EntryKind.Expense, DefaultCategories.Groceries);

        var summary = await db.Summaries.GetRangeSummaryAsync(DateRange.Month(2026, 8), CancellationToken.None);

        Assert.Equal(2000m, summary.TotalIncome);
        Assert.Equal(1692.50m, summary.TotalExpense);
        Assert.Equal(307.50m, summary.Net);
    }

    [Fact]
    public async Task Entries_outside_the_range_are_excluded()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 7, 31), 500m, EntryKind.Income, DefaultCategories.Salary);
        await db.AddAsync(new DateOnly(2026, 8, 1), 100m, EntryKind.Income, DefaultCategories.Salary);
        await db.AddAsync(new DateOnly(2026, 9, 1), 700m, EntryKind.Income, DefaultCategories.Salary);

        var summary = await db.Summaries.GetRangeSummaryAsync(DateRange.Month(2026, 8), CancellationToken.None);

        Assert.Equal(100m, summary.TotalIncome);
    }

    [Fact]
    public async Task Daily_buckets_land_on_the_right_day()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 5), 80m, EntryKind.Expense, DefaultCategories.Groceries);

        var summary = await db.Summaries.GetRangeSummaryAsync(new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10)), CancellationToken.None);

        Assert.Equal(BucketSize.Day, summary.Bucket);
        Assert.Equal(10, summary.Buckets.Count);
        var bucket = summary.Buckets.Single(b => b.Start == new DateOnly(2026, 8, 5));
        Assert.Equal(80m, bucket.Expense);
        Assert.Equal(80m, summary.Buckets.Sum(b => b.Expense));
    }

    [Fact]
    public async Task Weekly_buckets_group_a_three_month_range()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 6, 3), 10m, EntryKind.Expense, DefaultCategories.Fee);
        await db.AddAsync(new DateOnly(2026, 6, 5), 20m, EntryKind.Expense, DefaultCategories.Fee);

        var summary = await db.Summaries.GetRangeSummaryAsync(new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        Assert.Equal(BucketSize.Week, summary.Bucket);
        // June 1–7 holds both fees; the rest of the range is empty.
        var first = summary.Buckets[0];
        Assert.Equal(new DateOnly(2026, 6, 7), first.End);
        Assert.Equal(30m, first.Expense);
        Assert.Equal(30m, summary.TotalExpense);
    }

    [Fact]
    public async Task Balance_rises_with_income_and_falls_with_expenses()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 2), 1000m, EntryKind.Income, DefaultCategories.Salary);
        await db.AddAsync(new DateOnly(2026, 8, 4), 250m, EntryKind.Expense, DefaultCategories.Rent);
        await db.AddAsync(new DateOnly(2026, 8, 5), 100m, EntryKind.Income, DefaultCategories.Tips);

        var summary = await db.Summaries.GetRangeSummaryAsync(new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 6)), CancellationToken.None);

        Assert.Equal(
            [0m, 1000m, 1000m, 750m, 850m, 850m],
            summary.Buckets.Select(b => b.ClosingBalance));
        Assert.Equal(850m, summary.ClosingBalance);
    }

    [Fact]
    public async Task Balance_opens_from_entries_recorded_before_the_range()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 6, 10), 5000m, EntryKind.Income, DefaultCategories.Salary);
        await db.AddAsync(new DateOnly(2026, 7, 3), 2000m, EntryKind.Expense, DefaultCategories.Rent);
        await db.AddAsync(new DateOnly(2026, 8, 2), 400m, EntryKind.Expense, DefaultCategories.Utilities);

        var summary = await db.Summaries.GetRangeSummaryAsync(DateRange.Month(2026, 8), CancellationToken.None);

        Assert.Equal(3000m, summary.OpeningBalance);
        Assert.Equal(2600m, summary.ClosingBalance);
        Assert.Equal(3000m, summary.Buckets[0].ClosingBalance);
        Assert.Equal(2600m, summary.Buckets[^1].ClosingBalance);
    }

    [Fact]
    public async Task Balance_goes_negative_when_spending_outruns_income()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 1), 100m, EntryKind.Income, DefaultCategories.Tips);
        await db.AddAsync(new DateOnly(2026, 8, 2), 450m, EntryKind.Expense, DefaultCategories.Rent);

        var summary = await db.Summaries.GetRangeSummaryAsync(new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3)), CancellationToken.None);

        Assert.Equal(-350m, summary.ClosingBalance);
        Assert.Contains(summary.Buckets, b => b.ClosingBalance < 0);
    }

    [Fact]
    public async Task Weekly_balance_tracks_the_end_of_each_week()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 6, 2), 900m, EntryKind.Income, DefaultCategories.Salary);
        await db.AddAsync(new DateOnly(2026, 6, 9), 300m, EntryKind.Expense, DefaultCategories.Rent);

        var summary = await db.Summaries.GetRangeSummaryAsync(new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)), CancellationToken.None);

        Assert.Equal(BucketSize.Week, summary.Bucket);
        Assert.Equal(900m, summary.Buckets[0].ClosingBalance);
        Assert.Equal(600m, summary.Buckets[1].ClosingBalance);
        // Nothing after week two, so the line stays flat to the end of the range.
        Assert.All(summary.Buckets.Skip(1), b => Assert.Equal(600m, b.ClosingBalance));
    }

    [Fact]
    public async Task Sample_data_seeds_a_ledger_that_runs_in_both_directions()
    {
        using var db = new TestDatabase(Today, seedSample: true);

        var behind = await db.Summaries.GetRangeSummaryAsync(
            new DateRange(Today.AddDays(-89), Today), CancellationToken.None);
        var ahead = await db.Summaries.GetRangeSummaryAsync(
            new DateRange(Today, Today.AddDays(89)), CancellationToken.None);

        // A month of history behind today, and the bills and one-offs still to come.
        Assert.True(behind.TotalIncome > 0);
        Assert.True(behind.TotalExpense > 0);
        Assert.True(ahead.TotalIncome > 0);
        Assert.True(ahead.TotalExpense > 0);

        // Most of it is repeating, so the stored rows are far fewer than what is drawn.
        var stored = await db.Entries.CountAsync(CancellationToken.None);
        var visible = await db.Queries.GetAsync(
            new EntryFilter(Range: new DateRange(Today.AddDays(-89), Today.AddDays(89))),
            CancellationToken.None);
        Assert.True(stored > 0);
        Assert.True(visible.Count > stored);
    }
}
