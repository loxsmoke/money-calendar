using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MoneyCalendar.Services;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Services;
using SkiaSharp;

namespace MoneyCalendar.ViewModels;

/// <summary>
/// Summary section: income and expense totals for a range, the chart that breaks them down
/// with the running balance, and the transactions behind them.
/// </summary>
public partial class SummaryViewModel : RangePageViewModel
{
    private static readonly SKColor IncomeColor = SKColor.Parse("#1B7F3B");
    private static readonly SKColor ExpenseColor = SKColor.Parse("#C2603A");
    private static readonly SKColor BalanceColor = SKColor.Parse("#2E9E52");

    // Red is reserved for over-budget states (see Themes/Semantic.axaml), and a balance that has
    // gone under zero is exactly that: the line turns the error red, and the water it is under is
    // painted in the pink the error banners use.
    private static readonly SKColor DeficitColor = SKColor.Parse("#C62828");
    private static readonly SKColor DeficitFillColor = SKColor.Parse("#FDE7E7").WithAlpha(0xCC);
    private static readonly SKColor GridColor = SKColor.Parse("#D8D8D4");

    /// <summary>
    /// Pixels of air around each bar. It ends up as the gap between the income and expense bars
    /// of one day and, equally, between a day's expense bar and the next day's income bar.
    /// </summary>
    private const double BarGap = 2;

    private readonly ISummaryService _summaries;
    private readonly IEntryQueryService _entries;
    private readonly ICategoryRepository _categories;
    private readonly INavigationService _navigation;

    private Dictionary<Guid, Category> _categoryIndex = [];

    public override string Title => "Summary";

    // ---- chart ------------------------------------------------------------
    [ObservableProperty] private ISeries[] _chartSeries = [];
    [ObservableProperty] private Axis[] _chartXAxes = [];
    [ObservableProperty] private Axis[] _chartYAxes = [];
    [ObservableProperty] private string _bucketLabel = "";
    [ObservableProperty] private string _balanceLineText = "";

    // ---- totals -----------------------------------------------------------
    [ObservableProperty] private string _incomeTotalText = "";
    [ObservableProperty] private string _expenseTotalText = "";
    [ObservableProperty] private string _netTotalText = "";
    [ObservableProperty] private bool _netIsNegative;

    // ---- list -------------------------------------------------------------
    public ObservableCollection<EntryRowViewModel> Entries { get; } = [];
    [ObservableProperty] private string _listSubtitleText = "";
    [ObservableProperty] private bool _listIsEmpty;

    public SummaryViewModel(
        ISummaryService summaries,
        IEntryQueryService entries,
        ICategoryRepository categories,
        INavigationService navigation,
        IClock clock,
        ISettingsStore settings)
        : base(clock, settings)
    {
        _summaries = summaries;
        _entries = entries;
        _categories = categories;
        _navigation = navigation;
        InitializeRange();
    }

    protected override async Task<bool> LoadAsync(CancellationToken ct)
    {
        UpdateRangeText();
        var range = CurrentRange;
        _categoryIndex = (await _categories.GetAllAsync(ct)).ToDictionary(c => c.Id);

        var summary = await _summaries.GetRangeSummaryAsync(range, ct);
        BuildChart(summary);
        BuildTotals(summary);
        await BuildListAsync(range, ct);

        return summary.Buckets.Count > 0 && (summary.TotalIncome > 0 || summary.TotalExpense > 0 || Entries.Count > 0);
    }

    // ---- chart ------------------------------------------------------------

    private void BuildChart(RangeSummary summary)
    {
        var buckets = summary.Buckets;
        BucketLabel = summary.Bucket == BucketSize.Day ? "Daily" : "Weekly";
        BalanceLineText = $"Balance {Format.Money(summary.ClosingBalance, CurrencyCode)}";

        var balance = BalancePoints(summary);

        ChartSeries =
        [
            // The two bars split their day's width evenly and fill it. LiveCharts hands each
            // series half the column and insets the bar by Padding, which puts BarGap between
            // the pair and the same BarGap between one day's expense bar and the next day's
            // income bar — so the eye reads the pairs, not accidental groupings. Filling the
            // half also lands the income bar on the day's left edge, where the balance line
            // steps. No MaxBarWidth: a cap re-centres the bars inside their half and the even
            // spacing goes with it.
            new ColumnSeries<double>
            {
                Name = "Income",
                Values = buckets.Select(b => (double)b.Income).ToArray(),
                Fill = new SolidColorPaint(IncomeColor),
                Padding = BarGap,
                Rx = 3,
                Ry = 3,
            },
            new ColumnSeries<double>
            {
                Name = "Expenses",
                Values = buckets.Select(b => (double)b.Expense).ToArray(),
                Fill = new SolidColorPaint(ExpenseColor),
                Padding = BarGap,
                Rx = 3,
                Ry = 3,
            },
            // Balance shares the bars' axis, so its height can be read against them directly:
            // a bar taller than the line is a bucket that moves more money than is left over.
            //
            // The staircase is drawn corner by corner (see BalancePoints) rather than handed to
            // a step series, because where the riser lands is the whole point and that is not
            // ours to choose when the series decides it.
            new LineSeries<ObservablePoint>
            {
                Name = "Balance",
                Values = balance,
                Stroke = new SolidColorPaint(BalanceColor) { StrokeThickness = 2.5f },
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                GeometrySize = 0,
                LineSmoothness = 0,
            },
            // The same staircase again, but only where it runs below zero, drawn over the green
            // one: red line, pink water between it and the zero level. A second series rather
            // than a per-point colour because a line series carries one stroke, and it is hidden
            // from tooltips so hovering still reports a single balance.
            new LineSeries<ObservablePoint>
            {
                Name = "Deficit",
                Values = DeficitPoints(balance),
                Stroke = new SolidColorPaint(DeficitColor) { StrokeThickness = 2.5f },
                Fill = new SolidColorPaint(DeficitFillColor),
                GeometryFill = null,
                GeometryStroke = null,
                GeometrySize = 0,
                LineSmoothness = 0,
                IsHoverable = false,
            },
        ];

        ChartXAxes =
        [
            new Axis
            {
                Labels = buckets.Select(b => BucketLabelText(b, summary.Bucket)).ToArray(),
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 11,
                LabelsRotation = buckets.Count > 12 ? 45 : 0,
                // A hairline between every column, so a day (or week) can be read off the chart.
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 1 },
                MinStep = 1,
                ForceStepToMin = true,
                SeparatorsAtCenter = false,
                TicksAtCenter = false,
            },
        ];
        // One axis for everything. Bars never go below zero, but the balance can, so the floor
        // drops to whatever the lowest balance on the line is rather than being pinned at zero.
        var lowestBalance = balance.Count == 0
            ? 0d
            : Math.Min(0d, balance.Min(p => p.Y ?? 0d));

        ChartYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 11,
                MinLimit = lowestBalance,
                Labeler = v => v.ToString("N0", CultureInfo.CurrentCulture),
            },
        ];
    }

    /// <summary>
    /// The balance staircase, as explicit corners in chart coordinates rather than one value
    /// per bucket index.
    ///
    /// Columns are centred on whole indexes, so bucket i occupies [i - 0.5, i + 0.5]. The level
    /// drawn across a column is the balance **as the bucket opens** — what you had before that
    /// day's money moved. The first column therefore sits at the balance carried in from before
    /// the range, and each riser stands on the boundary where one bucket hands over to the next.
    /// The closing balance of the last bucket is the final riser, at the right-hand boundary.
    ///
    /// Read it as "what was in the account when this day started"; a bar and the step it causes
    /// meet at that bar's right-hand edge. Levelling each column at its own closing balance
    /// instead moves the whole line one bucket earlier — the first column would open at a
    /// balance that only exists once the first day is over.
    ///
    /// Spanning boundary to boundary also means no blank half-column at either edge of the plot.
    /// </summary>
    private static IReadOnlyList<ObservablePoint> BalancePoints(RangeSummary summary)
    {
        var buckets = summary.Buckets;
        if (buckets.Count == 0)
            return [];

        var points = new List<ObservablePoint>((buckets.Count * 2) + 1);
        for (var i = 0; i < buckets.Count; i++)
        {
            var opening = i == 0
                ? (double)summary.OpeningBalance
                : (double)buckets[i].ClosingBalance;
            points.Add(new ObservablePoint(i - 0.5, opening));
            points.Add(new ObservablePoint(i + 0.5, opening));
        }

        points.Add(new ObservablePoint(buckets.Count - 0.5, (double)buckets[^1].ClosingBalance));
        return points;
    }

    /// <summary>
    /// The stretch of the staircase that is under water, as a polyline of its own: the points
    /// below zero, the exact crossings where the line passes through zero, and an empty point
    /// everywhere else so each dip is drawn as its own path rather than joined to the next.
    ///
    /// A line series closes its fill at zero, so filling this one paints the deficit itself —
    /// from the line up to the zero level, and nothing above it.
    /// </summary>
    internal static IReadOnlyList<ObservablePoint> DeficitPoints(IReadOnlyList<ObservablePoint> balance)
    {
        var deficit = new List<ObservablePoint>(balance.Count + 4);
        var anyBelow = false;

        for (var i = 0; i < balance.Count; i++)
        {
            var x = balance[i].X ?? 0d;
            var y = balance[i].Y ?? 0d;

            // A crossing belongs to both sides of the line: it closes the dip that ends here, or
            // opens the one that starts here. The two points of a riser share an x, so a step
            // straight through zero puts the crossing on the riser itself.
            if (i > 0 && balance[i - 1].Y is { } previousY && previousY < 0 != y < 0)
            {
                var previousX = balance[i - 1].X ?? 0d;
                var t = (0d - previousY) / (y - previousY);
                deficit.Add(new ObservablePoint(previousX + (t * (x - previousX)), 0d));
            }

            if (y < 0)
            {
                deficit.Add(new ObservablePoint(x, y));
                anyBelow = true;
            }
            else
            {
                deficit.Add(new ObservablePoint(x, null));
            }
        }

        return anyBelow ? deficit : [];
    }

    private static string BucketLabelText(BucketTotals bucket, BucketSize size) =>
        size == BucketSize.Day
            ? bucket.Start.ToString("MMM d", CultureInfo.CurrentCulture)
            : $"{bucket.Start.ToString("MMM d", CultureInfo.CurrentCulture)}–{bucket.End.Day.ToString(CultureInfo.CurrentCulture)}";

    private void BuildTotals(RangeSummary summary)
    {
        IncomeTotalText = Format.Money(summary.TotalIncome, CurrencyCode);
        ExpenseTotalText = Format.Money(summary.TotalExpense, CurrencyCode);
        NetTotalText = Format.Money(summary.Net, CurrencyCode, explicitSign: true);
        NetIsNegative = summary.Net < 0;
    }

    // ---- list -------------------------------------------------------------

    private async Task BuildListAsync(DateRange range, CancellationToken ct)
    {
        var rows = await _entries.GetAsync(new EntryFilter(Range: range), ct);

        Entries.Clear();
        foreach (var entry in rows)
            Entries.Add(new EntryRowViewModel(entry, _categoryIndex.GetValueOrDefault(entry.CategoryId), CurrencyCode));

        var income = rows.Where(e => e.Kind == EntryKind.Income).Sum(e => e.Amount);
        var expense = rows.Where(e => e.Kind == EntryKind.Expense).Sum(e => e.Amount);
        ListSubtitleText = rows.Count == 0
            ? "Nothing recorded here yet."
            : $"{Format.Count(rows.Count, "entry", "entries")} · {Format.Money(income, CurrencyCode)} in · " +
              $"{Format.Money(expense, CurrencyCode)} out";
        ListIsEmpty = rows.Count == 0;
    }

    // ---- commands ---------------------------------------------------------

    /// <summary>"Add income" / "Add expense": open the matching ledger over the same range.</summary>
    [RelayCommand]
    private void OpenIncome() => _navigation.NavigateToLedger(EntryKind.Income, CurrentRange);

    [RelayCommand]
    private void OpenExpenses() => _navigation.NavigateToLedger(EntryKind.Expense, CurrentRange);
}
