using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Services;

/// <summary>
/// Aggregates entries into what the Summary section draws. Everything is computed in memory
/// from one range query: the prototype's data volumes are small, and SQLite cannot sum the
/// TEXT-stored decimals without a cast anyway.
/// </summary>
public sealed class SummaryService(IEntryQueryService entries) : ISummaryService
{
    public async Task<RangeSummary> GetRangeSummaryAsync(
        DateRange range, CancellationToken ct)
    {
        var rows = await entries.GetAsync(new EntryFilter(Range: range), ct).ConfigureAwait(false);
        var bucket = RangePolicy.BucketFor(range);

        var byDay = rows
            .GroupBy(e => e.Date)
            .ToDictionary(
                g => g.Key,
                g => new DayTotals(
                    g.Key,
                    g.Where(e => e.Kind == EntryKind.Income).Sum(e => e.Amount),
                    g.Where(e => e.Kind == EntryKind.Expense).Sum(e => e.Amount),
                    g.Count()));

        var days = range.Days()
            .Select(d => byDay.TryGetValue(d, out var totals) ? totals : new DayTotals(d, 0m, 0m, 0))
            .ToList();

        // The balance line starts from everything recorded before the range, so it reads as the
        // account balance rather than "net since the range began".
        var openingBalance = await GetBalanceBeforeAsync(range.From, ct).ConfigureAwait(false);

        var runningBalance = openingBalance;
        var buckets = RangePolicy.Buckets(range, bucket)
            .Select(b =>
            {
                var window = days.Where(d => b.Contains(d.Date)).ToList();
                var income = window.Sum(d => d.Income);
                var expense = window.Sum(d => d.Expense);
                runningBalance += income - expense;
                return new BucketTotals(b.From, b.To, income, expense, runningBalance);
            })
            .ToList();

        return new RangeSummary(
            range,
            bucket,
            buckets,
            days,
            rows.Where(e => e.Kind == EntryKind.Income).Sum(e => e.Amount),
            rows.Where(e => e.Kind == EntryKind.Expense).Sum(e => e.Amount),
            openingBalance);
    }

    /// <summary>
    /// Net of everything dated before <paramref name="from"/>. The prototype has no opening
    /// balance to configure, so the balance line starts at zero before the first entry.
    /// </summary>
    private async Task<decimal> GetBalanceBeforeAsync(DateOnly from, CancellationToken ct)
    {
        if (from == DateOnly.MinValue)
            return 0m;

        var history = new DateRange(DateOnly.MinValue, from.AddDays(-1));
        var rows = await entries.GetAsync(new EntryFilter(Range: history), ct).ConfigureAwait(false);
        return rows.Sum(e => e.SignedAmount);
    }
}
