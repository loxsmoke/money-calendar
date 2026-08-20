using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Services;

/// <summary>
/// Range rules shared by every section: the Summary chart is capped at three months, and the
/// bucket width follows from the range length so the chart never draws more than ~62 columns.
/// </summary>
public static class RangePolicy
{
    /// <summary>
    /// Six months, counted generously. The range dropdowns reach three months back and three
    /// months forward, which spans five calendar months at most; the cap only bites on
    /// hand-picked dates well outside that.
    /// </summary>
    public const int MaxDays = 186;

    /// <summary>
    /// Ranges up to two months get one column per day; longer ranges get weekly columns. Two
    /// months of daily bars is dense but still readable, and a day-per-column chart is what
    /// makes the balance line step on the day the money actually moves.
    /// </summary>
    public const int DailyBucketMaxDays = 62;

    /// <summary>Orders the endpoints and trims the tail so the range never exceeds three months.</summary>
    public static DateRange Clamp(DateOnly from, DateOnly to)
    {
        if (to < from)
            (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
            to = from.AddDays(MaxDays - 1);
        return new DateRange(from, to);
    }

    public static bool ExceedsMaximum(DateOnly from, DateOnly to) =>
        Math.Abs(to.DayNumber - from.DayNumber) + 1 > MaxDays;

    public static BucketSize BucketFor(DateRange range) =>
        range.DayCount <= DailyBucketMaxDays ? BucketSize.Day : BucketSize.Week;

    public static int DaysPerBucket(BucketSize bucket) => bucket == BucketSize.Day ? 1 : 7;

    /// <summary>Bucket boundaries, aligned to the start of the range.</summary>
    public static IReadOnlyList<DateRange> Buckets(DateRange range, BucketSize bucket)
    {
        var width = DaysPerBucket(bucket);
        var buckets = new List<DateRange>();
        for (var start = range.From; start <= range.To; start = start.AddDays(width))
        {
            var end = start.AddDays(width - 1);
            buckets.Add(new DateRange(start, end > range.To ? range.To : end));
        }
        return buckets;
    }
}
