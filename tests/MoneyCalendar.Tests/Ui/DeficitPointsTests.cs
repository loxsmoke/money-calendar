using LiveChartsCore.Defaults;
using MoneyCalendar.ViewModels;

namespace MoneyCalendar.Tests.Ui;

/// <summary>
/// The balance line turns red and fills the water under it when the running balance goes
/// negative. That is one series overlaid on the green one, so what matters is that it carries
/// only the part of the staircase below zero, breaks where the balance surfaces, and starts and
/// ends exactly on the zero level — a fill that begins a step early paints water over a day
/// that was still solvent.
/// </summary>
public class DeficitPointsTests
{
    private static List<ObservablePoint> Line(params double[] values)
    {
        var points = new List<ObservablePoint>();
        for (var i = 0; i < values.Length; i++)
        {
            points.Add(new ObservablePoint(i - 0.5, values[i]));
            points.Add(new ObservablePoint(i + 0.5, values[i]));
        }

        return points;
    }

    [Fact]
    public void A_balance_that_never_dips_draws_nothing()
    {
        Assert.Empty(SummaryViewModel.DeficitPoints(Line(120, 0, 40)));
        Assert.Empty(SummaryViewModel.DeficitPoints([]));
    }

    [Fact]
    public void Only_the_points_below_zero_are_drawn()
    {
        var deficit = SummaryViewModel.DeficitPoints(Line(50, -20, 30));

        var drawn = deficit.Where(p => p.Y is not null).ToList();
        Assert.All(drawn, p => Assert.True(p.Y <= 0));
        Assert.Contains(drawn, p => p.Y == -20);

        // The surfaced buckets are gaps, so the dip is its own path.
        Assert.Contains(deficit, p => p.Y is null);
    }

    [Fact]
    public void The_dip_opens_and_closes_on_the_zero_level()
    {
        // The staircase steps down at x = 0.5 and back up at x = 1.5, both times through zero.
        var deficit = SummaryViewModel.DeficitPoints(Line(50, -20, 30));
        var drawn = deficit.Where(p => p.Y is not null).ToList();

        Assert.Equal(0d, drawn[0].Y!.Value);
        Assert.Equal(0.5d, drawn[0].X!.Value);
        Assert.Equal(0d, drawn[^1].Y!.Value);
        Assert.Equal(1.5d, drawn[^1].X!.Value);
    }

    [Fact]
    public void A_balance_that_is_under_water_throughout_is_drawn_whole()
    {
        var balance = Line(-10, -40, -5);

        var deficit = SummaryViewModel.DeficitPoints(balance);

        Assert.Equal(balance.Count, deficit.Count);
        Assert.All(deficit, p => Assert.NotNull(p.Y));
    }

    [Fact]
    public void Two_separate_dips_stay_separated()
    {
        var deficit = SummaryViewModel.DeficitPoints(Line(-10, 20, -30));

        // A gap between them; without it the two dips would be joined across the solvent bucket.
        var gapIndex = deficit.Select((point, index) => (point, index)).First(p => p.point.Y is null).index;
        Assert.InRange(gapIndex, 1, deficit.Count - 2);
        Assert.Contains(deficit.Take(gapIndex), p => p.Y == -10);
        Assert.Contains(deficit.Skip(gapIndex), p => p.Y == -30);
    }

    [Fact]
    public void A_balance_resting_on_zero_is_not_a_deficit()
    {
        Assert.Empty(SummaryViewModel.DeficitPoints(Line(0, 0)));
    }
}
