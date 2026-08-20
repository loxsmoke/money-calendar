using Avalonia.Controls;
using MoneyCalendar.App.Controls;

namespace MoneyCalendar.Tests.Ui;

[Collection("Headless")]
public class DateFieldTests(HeadlessSessionFixture fixture)
{
    private Task Run(Action test) => fixture.Session.Dispatch(() =>
    {
        test();
        return true;
    }, CancellationToken.None);

    private static DateField Show(DateOnly selected)
    {
        var field = new DateField
        {
            SelectedDate = new DateTimeOffset(selected.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        };
        // The control builds its grid eagerly, but a window keeps it in a realistic tree.
        new Window { Content = field }.Show();
        return field;
    }

    [Fact]
    public Task Grid_covers_whole_weeks_around_the_selected_month() => Run(() =>
    {
        var field = Show(new DateOnly(2026, 8, 19));

        Assert.True(field.Days.Count % 7 == 0);
        Assert.Equal(31, field.Days.Count(d => d.IsInMonth));
        Assert.Contains(field.Days, d => d.Date == new DateOnly(2026, 8, 1) && d.IsInMonth);
        Assert.Contains(field.Days, d => d.Date == new DateOnly(2026, 8, 31) && d.IsInMonth);
        // Leading and trailing padding belongs to the neighbouring months.
        Assert.Contains(field.Days, d => !d.IsInMonth);
    });

    [Fact]
    public Task Selected_day_is_marked_and_shown_on_the_trigger() => Run(() =>
    {
        var field = Show(new DateOnly(2026, 8, 19));

        var selected = Assert.Single(field.Days, d => d.IsSelected);
        Assert.Equal(new DateOnly(2026, 8, 19), selected.Date);
        Assert.Contains("19", field.TriggerLabel, StringComparison.Ordinal);
        Assert.Contains("2026", field.TriggerLabel, StringComparison.Ordinal);
        Assert.Contains("August", field.MonthLabelText, StringComparison.Ordinal);
    });

    [Fact]
    public Task Stepping_months_moves_the_grid_without_changing_the_selection() => Run(() =>
    {
        var field = Show(new DateOnly(2026, 8, 19));

        field.ShowMonth(new DateOnly(2026, 7, 1));

        Assert.Contains("July", field.MonthLabelText, StringComparison.Ordinal);
        Assert.Equal(31, field.Days.Count(d => d.IsInMonth));
        Assert.Contains(field.Days, d => d.Date == new DateOnly(2026, 7, 15) && d.IsInMonth);
        // Still August the 19th until a day is actually clicked.
        Assert.Equal(new DateOnly(2026, 8, 19), DateOnly.FromDateTime(field.SelectedDate.Date));
    });

    [Fact]
    public Task Stepping_across_a_year_boundary_works() => Run(() =>
    {
        var field = Show(new DateOnly(2026, 1, 15));

        field.ShowMonth(new DateOnly(2026, 1, 1).AddMonths(-1));

        Assert.Contains("December", field.MonthLabelText, StringComparison.Ordinal);
        Assert.Contains("2025", field.MonthLabelText, StringComparison.Ordinal);
        Assert.Equal(31, field.Days.Count(d => d.IsInMonth));
    });

    [Fact]
    public Task Picking_a_day_updates_the_selection_and_the_grid() => Run(() =>
    {
        var field = Show(new DateOnly(2026, 8, 19));

        field.PickDate(new DateOnly(2026, 8, 3));

        Assert.Equal(new DateOnly(2026, 8, 3), DateOnly.FromDateTime(field.SelectedDate.Date));
        var selected = Assert.Single(field.Days, d => d.IsSelected);
        Assert.Equal(new DateOnly(2026, 8, 3), selected.Date);
        Assert.Contains("3", field.TriggerLabel, StringComparison.Ordinal);
    });

    [Fact]
    public Task Picking_a_day_from_the_padding_jumps_to_that_month() => Run(() =>
    {
        var field = Show(new DateOnly(2026, 8, 19));
        var padded = field.Days.First(d => !d.IsInMonth);

        field.PickDate(padded.Date);

        Assert.Equal(padded.Date, DateOnly.FromDateTime(field.SelectedDate.Date));
        // The grid follows the selection, so the picked day is now inside the shown month.
        Assert.Contains(field.Days, d => d.Date == padded.Date && d.IsInMonth && d.IsSelected);
    });

    [Fact]
    public Task February_in_a_leap_year_has_29_days() => Run(() =>
    {
        var field = Show(new DateOnly(2028, 2, 10));

        Assert.Equal(29, field.Days.Count(d => d.IsInMonth));
        Assert.Contains(field.Days, d => d.Date == new DateOnly(2028, 2, 29) && d.IsInMonth);
    });
}
