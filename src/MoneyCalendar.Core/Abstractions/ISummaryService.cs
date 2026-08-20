using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Abstractions;

/// <summary>
/// One chart column: totals for the bucket starting at <paramref name="Start"/>, plus the
/// account balance as of <paramref name="End"/> (opening balance + every net up to here).
/// </summary>
public sealed record BucketTotals(
    DateOnly Start, DateOnly End, decimal Income, decimal Expense, decimal ClosingBalance)
{
    public decimal Net => Income - Expense;
}

/// <summary>Per-day totals behind the calendar pills.</summary>
public sealed record DayTotals(DateOnly Date, decimal Income, decimal Expense, int EntryCount);

/// <summary>
/// Everything the Summary section draws for one range: the bars, the running balance, and the
/// calendar day totals.
/// </summary>
public sealed record RangeSummary(
    DateRange Range,
    BucketSize Bucket,
    IReadOnlyList<BucketTotals> Buckets,
    IReadOnlyList<DayTotals> Days,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal OpeningBalance)
{
    public decimal Net => TotalIncome - TotalExpense;

    /// <summary>Balance at the end of the range: everything recorded up to and including it.</summary>
    public decimal ClosingBalance => OpeningBalance + Net;
}

public interface ISummaryService
{
    Task<RangeSummary> GetRangeSummaryAsync(DateRange range, CancellationToken ct);
}
