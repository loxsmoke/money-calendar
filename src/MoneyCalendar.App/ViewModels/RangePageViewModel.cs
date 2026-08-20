using CommunityToolkit.Mvvm.ComponentModel;
using MoneyCalendar.App.Services;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Services;

namespace MoneyCalendar.App.ViewModels;

/// <summary>
/// One end of the range, expressed in whole months relative to the current one:
/// -2 is "start two months back", +2 is "end two months ahead". A null offset is Custom —
/// the date came from the date field rather than the dropdown.
/// </summary>
public sealed record RangeOption(string Label, int? MonthOffset)
{
    public bool IsCustom => MonthOffset is null;

    public override string ToString() => Label;
}

/// <summary>
/// Shared range selection for Summary, Income and Expenses. Each end has its own dropdown —
/// how far back to start, how far forward to end — beside a date field that can override it.
/// Editing a date switches that end (only that end) to Custom.
/// </summary>
public abstract partial class RangePageViewModel(IClock clock, ISettingsStore settings) : PageViewModel
{
    private bool _suppressReload;

    protected IClock Clock { get; } = clock;
    protected ISettingsStore Settings { get; } = settings;
    protected string CurrencyCode => Settings.Current.CurrencyCode;

    /// <summary>Where the range starts: the first day of the month this many months back.</summary>
    public IReadOnlyList<RangeOption> BackRangeOptions { get; } =
    [
        new("This month", 0),
        new("Last 2 months", -1),
        new("Last 3 months", -2),
        new("Custom", null),
    ];

    /// <summary>Where the range ends: the last day of the month this many months ahead.</summary>
    public IReadOnlyList<RangeOption> ForwardRangeOptions { get; } =
    [
        new("This month", 0),
        new("Next 2 months", 1),
        new("Next 3 months", 2),
        new("Custom", null),
    ];

    [ObservableProperty] private RangeOption? _selectedBackRange;
    [ObservableProperty] private RangeOption? _selectedForwardRange;
    [ObservableProperty] private DateTimeOffset _startDate;
    [ObservableProperty] private DateTimeOffset _endDate;
    [ObservableProperty] private string _rangeLabel = "";

    /// <summary>Set when the picked range was trimmed to the maximum span.</summary>
    [ObservableProperty] private string? _rangeNotice;

    /// <summary>The effective (ordered, clamped) range every load queries.</summary>
    public DateRange CurrentRange => RangePolicy.Clamp(
        DateOnly.FromDateTime(StartDate.Date), DateOnly.FromDateTime(EndDate.Date));

    /// <summary>Applies the default selection — this month at both ends.</summary>
    protected void InitializeRange()
    {
        _suppressReload = true;
        try
        {
            SelectedBackRange = BackRangeOptions[0];
            SelectedForwardRange = ForwardRangeOptions[0];
            ApplyBack(BackRangeOptions[0]);
            ApplyForward(ForwardRangeOptions[0]);
        }
        finally
        {
            _suppressReload = false;
        }
    }

    /// <summary>Deep link from the Summary calendar: show exactly this range.</summary>
    public void ApplyExternalRange(DateRange range)
    {
        _suppressReload = true;
        try
        {
            SelectedBackRange = BackRangeOptions[^1];
            SelectedForwardRange = ForwardRangeOptions[^1];
            StartDate = ToOffset(range.From);
            EndDate = ToOffset(range.To);
            UpdateRangeText();
        }
        finally
        {
            _suppressReload = false;
        }
    }

    partial void OnSelectedBackRangeChanged(RangeOption? value)
    {
        if (value is null)
            return;

        ApplyBack(value);
        UpdateRangeText();
        RequestReload();
    }

    partial void OnSelectedForwardRangeChanged(RangeOption? value)
    {
        if (value is null)
            return;

        ApplyForward(value);
        UpdateRangeText();
        RequestReload();
    }

    partial void OnStartDateChanged(DateTimeOffset value) =>
        OnBoundaryChanged(back: true);

    partial void OnEndDateChanged(DateTimeOffset value) =>
        OnBoundaryChanged(back: false);

    /// <summary>A hand-picked date makes that end Custom, whatever its dropdown said.</summary>
    private void OnBoundaryChanged(bool back)
    {
        if (_suppressReload)
            return;

        _suppressReload = true;
        try
        {
            if (back)
                SelectedBackRange = BackRangeOptions[^1];
            else
                SelectedForwardRange = ForwardRangeOptions[^1];
        }
        finally
        {
            _suppressReload = false;
        }

        UpdateRangeText();
        RequestReload();
    }

    private void ApplyBack(RangeOption option)
    {
        if (option.MonthOffset is not { } offset)
            return;

        var month = Clock.Today.AddMonths(offset);
        SetBoundary(() => StartDate = ToOffset(new DateOnly(month.Year, month.Month, 1)));
    }

    private void ApplyForward(RangeOption option)
    {
        if (option.MonthOffset is not { } offset)
            return;

        var month = Clock.Today.AddMonths(offset);
        var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
        SetBoundary(() => EndDate = ToOffset(new DateOnly(month.Year, month.Month, lastDay)));
    }

    /// <summary>
    /// Moves a boundary with the change handler suppressed, so the end that the dropdown just
    /// set does not immediately flip itself to Custom.
    /// </summary>
    private void SetBoundary(Action write)
    {
        var wasSuppressed = _suppressReload;
        _suppressReload = true;
        try
        {
            write();
        }
        finally
        {
            _suppressReload = wasSuppressed;
        }
    }

    protected void UpdateRangeText()
    {
        var from = DateOnly.FromDateTime(StartDate.Date);
        var to = DateOnly.FromDateTime(EndDate.Date);
        RangeNotice = RangePolicy.ExceedsMaximum(from, to)
            ? $"Ranges are capped at {RangePolicy.MaxDays} days — showing the first {RangePolicy.MaxDays}."
            : null;

        var range = CurrentRange;
        RangeLabel = $"{Format.RangeText(range)}  ·  {Format.Count(range.DayCount, "day", "days")}";
    }

    protected void RequestReload()
    {
        if (!_suppressReload)
            _ = ReloadAsync();
    }

    protected static DateTimeOffset ToOffset(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
