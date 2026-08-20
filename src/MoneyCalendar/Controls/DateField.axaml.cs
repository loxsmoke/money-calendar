using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace MoneyCalendar.Controls;

/// <summary>One cell of the month grid inside <see cref="DateField"/>.</summary>
public sealed record DateFieldDay(DateOnly Date, string Text, bool IsInMonth, bool IsToday, bool IsSelected);

/// <summary>
/// A date field that drops down a real month calendar: ‹ › walk the months and a click picks
/// the day. Replaces Avalonia's DatePicker, whose spinner popup makes picking a specific date
/// a scroll-and-hunt exercise.
/// </summary>
public partial class DateField : UserControl
{
    /// <summary>The picked date. Two-way by default, so pages can bind their range endpoints.</summary>
    public static readonly StyledProperty<DateTimeOffset> SelectedDateProperty =
        AvaloniaProperty.Register<DateField, DateTimeOffset>(
            nameof(SelectedDate), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The month the grid is showing, which is not necessarily the selected month.</summary>
    private DateOnly _displayMonth;

    public DateField()
    {
        InitializeComponent();
        WeekdayHeaders.ItemsSource = BuildWeekdayHeaders();
        _displayMonth = FirstOfMonth(SelectedDay());
        if (Trigger.Flyout is Flyout flyout)
            flyout.Opened += (_, _) => ShowMonth(FirstOfMonth(SelectedDay()));
        Rebuild();
    }

    public DateTimeOffset SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>The month grid as it currently stands — six weeks, leading and trailing days included.</summary>
    internal IReadOnlyList<DateFieldDay> Days { get; private set; } = [];

    internal string TriggerLabel => TriggerText.Text ?? "";

    internal string MonthLabelText => MonthLabel.Text ?? "";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedDateProperty)
        {
            _displayMonth = FirstOfMonth(SelectedDay());
            Rebuild();
        }
    }

    /// <summary>Points the grid at a month without changing the selection.</summary>
    internal void ShowMonth(DateOnly month)
    {
        _displayMonth = FirstOfMonth(month);
        Rebuild();
    }

    /// <summary>Picks a date, as clicking a day cell does.</summary>
    internal void PickDate(DateOnly date)
    {
        SelectedDate = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private void OnPreviousMonth(object? sender, RoutedEventArgs e) => ShowMonth(_displayMonth.AddMonths(-1));

    private void OnNextMonth(object? sender, RoutedEventArgs e) => ShowMonth(_displayMonth.AddMonths(1));

    private void OnToday(object? sender, RoutedEventArgs e)
    {
        PickDate(DateOnly.FromDateTime(DateTime.Now));
        Close();
    }

    private void OnDayClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: DateFieldDay day })
            return;

        PickDate(day.Date);
        Close();
    }

    private void Close() => (Trigger.Flyout as Flyout)?.Hide();

    private void Rebuild()
    {
        var selected = SelectedDay();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var culture = CultureInfo.CurrentCulture;

        TriggerText.Text = selected.ToString("MMM d, yyyy", culture);
        MonthLabel.Text = _displayMonth.ToString("MMMM yyyy", culture);

        // Pad to whole weeks so the grid always has seven columns.
        var lead = ((int)_displayMonth.DayOfWeek - (int)culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var gridStart = _displayMonth.AddDays(-lead);
        var lastOfMonth = _displayMonth.AddMonths(1).AddDays(-1);
        var trail = 6 - (((int)lastOfMonth.DayOfWeek - (int)culture.DateTimeFormat.FirstDayOfWeek + 7) % 7);
        var gridEnd = lastOfMonth.AddDays(trail);

        var days = new List<DateFieldDay>();
        for (var date = gridStart; date <= gridEnd; date = date.AddDays(1))
        {
            days.Add(new DateFieldDay(
                date,
                date.Day.ToString(culture),
                IsInMonth: date.Month == _displayMonth.Month && date.Year == _displayMonth.Year,
                IsToday: date == today,
                IsSelected: date == selected));
        }

        Days = days;
        DayGrid.ItemsSource = days;
    }

    /// <summary>
    /// The selected date as a day. Falls back to today while the property still holds its
    /// default (a binding has not been applied yet), so the grid never starts in year one.
    /// </summary>
    private DateOnly SelectedDay() => SelectedDate.Year < 1900
        ? DateOnly.FromDateTime(DateTime.Now)
        : DateOnly.FromDateTime(SelectedDate.Date);

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static IReadOnlyList<string> BuildWeekdayHeaders()
    {
        var culture = CultureInfo.CurrentCulture;
        var first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        return Enumerable.Range(0, 7)
            .Select(i => culture.DateTimeFormat.ShortestDayNames[(first + i) % 7])
            .ToList();
    }
}
