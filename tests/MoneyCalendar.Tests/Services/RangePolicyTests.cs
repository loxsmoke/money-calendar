using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Services;

namespace MoneyCalendar.Tests.Services;

public class RangePolicyTests
{
    [Fact]
    public void Clamp_orders_reversed_endpoints()
    {
        var range = RangePolicy.Clamp(new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1));

        Assert.Equal(new DateOnly(2026, 8, 1), range.From);
        Assert.Equal(new DateOnly(2026, 8, 31), range.To);
    }

    [Fact]
    public void Clamp_trims_ranges_longer_than_the_maximum()
    {
        var range = RangePolicy.Clamp(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Equal(RangePolicy.MaxDays, range.DayCount);
        Assert.Equal(new DateOnly(2026, 1, 1), range.From);
    }

    [Fact]
    public void Clamp_leaves_a_maximum_length_range_alone()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = from.AddDays(RangePolicy.MaxDays - 1);

        var range = RangePolicy.Clamp(from, to);

        Assert.Equal(to, range.To);
        Assert.False(RangePolicy.ExceedsMaximum(from, to));
    }

    [Theory]
    [InlineData(1, BucketSize.Day)]
    [InlineData(31, BucketSize.Day)]
    [InlineData(62, BucketSize.Day)]
    [InlineData(63, BucketSize.Week)]
    [InlineData(92, BucketSize.Week)]
    [InlineData(153, BucketSize.Week)]
    public void Bucket_width_follows_range_length(int days, BucketSize expected)
    {
        var from = new DateOnly(2026, 3, 1);
        var range = new DateRange(from, from.AddDays(days - 1));

        Assert.Equal(expected, RangePolicy.BucketFor(range));
    }

    [Fact]
    public void Buckets_cover_the_range_without_overlap_or_gaps()
    {
        var range = new DateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 3));

        var buckets = RangePolicy.Buckets(range, BucketSize.Week);

        Assert.Equal(range.From, buckets[0].From);
        Assert.Equal(range.To, buckets[^1].To);
        for (var i = 1; i < buckets.Count; i++)
            Assert.Equal(buckets[i - 1].To.AddDays(1), buckets[i].From);
        Assert.Equal(range.DayCount, buckets.Sum(b => b.DayCount));
    }
}
