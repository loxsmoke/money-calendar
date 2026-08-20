using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneyCalendar.App.Services;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.App.ViewModels;

/// <summary>A category row in the "by type" breakdown at the top of a ledger.</summary>
public sealed record CategoryBreakdownItem(
    string Name, IBrush Color, string AmountText, string ShareText, double BarFraction, string CountText);

/// <summary>
/// Shared implementation of the Income and Expenses sections: a range summary on top and the
/// list of that kind's transactions underneath, with add/edit/delete and custom categories.
/// The two sections differ only in <see cref="Kind"/> and their labels.
/// </summary>
public abstract partial class LedgerViewModel : RangePageViewModel
{
    private readonly IEntryRepository _entries;
    private readonly IEntryQueryService _entryQueries;
    private readonly ICategoryRepository _categories;
    private readonly IAccountRepository _accounts;
    private readonly INavigationService _navigation;
    private CancellationTokenSource? _searchDelay;
    private IReadOnlyList<Category> _kindCategories = [];
    private IReadOnlyList<AccountChoice> _incomeAccounts = [];
    private IReadOnlyList<AccountChoice> _expenseAccounts = [];

    protected LedgerViewModel(
        EntryKind kind,
        IEntryRepository entries,
        IEntryQueryService entryQueries,
        ICategoryRepository categories,
        IAccountRepository accounts,
        INavigationService navigation,
        IClock clock,
        ISettingsStore settings)
        : base(clock, settings)
    {
        Kind = kind;
        _entries = entries;
        _entryQueries = entryQueries;
        _categories = categories;
        _accounts = accounts;
        _navigation = navigation;
        InitializeRange();
    }

    public EntryKind Kind { get; }
    public bool IsIncome => Kind == EntryKind.Income;

    /// <summary>"Type" for income, "Category" for expenses — the two sections label it differently.</summary>
    public string CategoryColumnHeader => IsIncome ? "Type" : "Category";
    public abstract string AddButtonText { get; }
    public abstract string EmptyStateText { get; }

    public ObservableCollection<EntryRowViewModel> Rows { get; } = [];
    [ObservableProperty] private IReadOnlyList<CategoryBreakdownItem> _breakdown = [];

    // ---- range summary ----------------------------------------------------
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private string _averagePerDayText = "";
    [ObservableProperty] private string _largestText = "";

    [ObservableProperty] private string? _searchText;

    /// <summary>Narrows the list to repeating entries only. Off by default.</summary>
    [ObservableProperty] private bool _showRepeatingOnly;
    [ObservableProperty] private EntryRowViewModel? _selectedRow;
    [ObservableProperty] private string? _statusText;

    public IReadOnlyList<CategoryChoice> CategoryChoices { get; private set; } = [];

    protected override async Task<bool> LoadAsync(CancellationToken ct)
    {
        UpdateRangeText();
        var range = CurrentRange;

        var all = await _categories.GetAllAsync(ct);
        _kindCategories = all.Where(c => c.Kind == Kind).ToList();
        CategoryChoices = _kindCategories
            .Select(c => new CategoryChoice(c.Id, c.Name, c.WantsAccountDetails))
            .ToList();
        OnPropertyChanged(nameof(CategoryChoices));

        // Money moves from an income account (checking, savings, investment, other income) to
        // an expense account (credit, mortgage, other expense); income only fills the first.
        var allAccounts = await _accounts.GetAllAsync(ct);
        _incomeAccounts = allAccounts
            .Where(a => AccountTypes.IsIncome(a.Type))
            .Select(a => new AccountChoice(a.Id, a.Name))
            .ToList();
        _expenseAccounts = allAccounts
            .Where(a => !AccountTypes.IsIncome(a.Type))
            .Select(a => new AccountChoice(a.Id, a.Name))
            .ToList();

        var index = _kindCategories.ToDictionary(c => c.Id);
        var rows = await _entryQueries.GetAsync(
            new EntryFilter(Range: range, Kind: Kind, Search: SearchText), ct);
        if (ShowRepeatingOnly)
            rows = rows.Where(e => e.IsRecurring).ToList();

        Rows.Clear();
        foreach (var entry in rows)
            Rows.Add(new EntryRowViewModel(entry, index.GetValueOrDefault(entry.CategoryId), CurrencyCode));

        BuildSummary(rows, range);
        BuildBreakdown(rows, index);

        return rows.Count > 0 || !string.IsNullOrWhiteSpace(SearchText) || ShowRepeatingOnly;
    }

    private void BuildSummary(IReadOnlyList<Entry> rows, DateRange range)
    {
        var total = rows.Sum(e => e.Amount);
        TotalText = Format.Money(total, CurrencyCode);
        CountText = Format.Count(rows.Count, "entry", "entries");
        AveragePerDayText = Format.Money(
            range.DayCount == 0 ? 0m : decimal.Round(total / range.DayCount, 2, MidpointRounding.AwayFromZero),
            CurrencyCode);
        LargestText = rows.Count == 0
            ? "—"
            : Format.Money(rows.Max(e => e.Amount), CurrencyCode);
    }

    /// <summary>
    /// The by-category bars are built from the rows on screen, so they always agree with the
    /// list — including when the search box or the repeating filter has narrowed it.
    /// </summary>
    private void BuildBreakdown(IReadOnlyList<Entry> rows, Dictionary<Guid, Category> index)
    {
        var totals = rows
            .GroupBy(e => e.CategoryId)
            .Select(g => new
            {
                Name = index.GetValueOrDefault(g.Key)?.Name ?? "Unknown",
                ColorHex = index.GetValueOrDefault(g.Key)?.ColorHex ?? "#8A8A92",
                Amount = g.Sum(e => e.Amount),
                Count = g.Count(),
            })
            .OrderByDescending(t => t.Amount)
            .ThenBy(t => t.Name, StringComparer.CurrentCulture)
            .ToList();

        var sum = totals.Sum(t => t.Amount);
        var max = totals.Count == 0 ? 0m : totals.Max(t => t.Amount);

        Breakdown = totals
            .Select(t => new CategoryBreakdownItem(
                t.Name,
                new SolidColorBrush(Color.Parse(t.ColorHex)),
                Format.Money(t.Amount, CurrencyCode),
                sum == 0 ? "—" : Format.Percent((double)(t.Amount / sum)),
                max == 0 ? 0 : (double)(t.Amount / max),
                Format.Count(t.Count, "entry", "entries")))
            .ToList();
    }

    // ---- editing ----------------------------------------------------------

    /// <summary>
    /// True when an entry of this kind cannot be recorded yet because an account it needs is
    /// missing: income needs an income account, an expense needs one of each. The view stops at
    /// a message rather than opening an editor that could never be saved.
    /// </summary>
    public bool RequiresAccounts =>
        _incomeAccounts.Count == 0 || (!IsIncome && _expenseAccounts.Count == 0);

    public string MissingAccountTitle => IsIncome
        ? "Add an income account first"
        : "Add an account first";

    /// <summary>
    /// Says which side is missing and names the types that count as it, straight from the
    /// domain list, so the message cannot drift from the rule the pickers apply.
    /// </summary>
    public string MissingAccountMessage
    {
        get
        {
            var lines = new List<string>
            {
                IsIncome
                    ? "Income has to land in an account, and none of your accounts is an income account."
                    : "An expense is paid from an income account to an expense account, and you are missing one of those.",
            };

            if (_incomeAccounts.Count == 0)
                lines.Add($"Income accounts are: {TypeList(income: true)}.");
            if (!IsIncome && _expenseAccounts.Count == 0)
                lines.Add($"Expense accounts are: {TypeList(income: false)}.");

            lines.Add(IsIncome
                ? "Add one in the Accounts section, then come back to record this income."
                : "Add what is missing in the Accounts section, then come back to record this expense.");
            return string.Join(Environment.NewLine + Environment.NewLine, lines);
        }
    }

    private static string TypeList(bool income) => string.Join(
        ", ",
        AccountTypes.All.Where(t => AccountTypes.IsIncome(t) == income).Select(AccountTypes.Label));

    /// <summary>Takes the user to the Accounts section to fix that.</summary>
    public void OpenAccounts() => _navigation.NavigateTo(PageKey.Accounts);

    /// <summary>
    /// Builds editor state for the dialog; the view owns the window, this owns the data. A row
    /// may be a projected occurrence of a series, so the stored template is fetched and edited
    /// instead — otherwise saving would move the series start onto the occurrence's date.
    /// </summary>
    public async Task<EntryEditorViewModel> CreateEditorAsync(EntryRowViewModel? row)
    {
        var defaultDate = row?.Date ?? DefaultNewEntryDate();
        var template = row?.Entry;
        if (template is { IsOccurrence: true })
            template = await _entries.GetByIdAsync(template.Id, CancellationToken.None) ?? template;

        return new EntryEditorViewModel(
            Kind, CategoryChoices, _incomeAccounts, _expenseAccounts, CurrencyCode, defaultDate, template);
    }

    /// <summary>New entries land on today when today is inside the range, otherwise on the range start.</summary>
    private DateOnly DefaultNewEntryDate()
    {
        var today = Clock.Today;
        return CurrentRange.Contains(today) ? today : CurrentRange.From;
    }

    /// <summary>Commits a dialog result. Returns false when validation failed.</summary>
    public async Task<bool> SaveEditorAsync(EntryEditorViewModel editor)
    {
        if (editor.TryBuild() is not { } entry)
            return false;

        if (editor.IsNew)
        {
            await _entries.AddAsync(entry, CancellationToken.None);
            StatusText = $"Added {Format.Money(entry.Amount, CurrencyCode)} on {Format.ShortDate(entry.Date)}.";
        }
        else
        {
            await _entries.UpdateAsync(entry, CancellationToken.None);
            StatusText = $"Updated {Format.Money(entry.Amount, CurrencyCode)} on {Format.ShortDate(entry.Date)}.";
        }

        await ReloadAsync();
        return true;
    }

    /// <summary>Deletes the row's entry — for a series, that is the whole series.</summary>
    public async Task DeleteAsync(EntryRowViewModel row)
    {
        await _entries.DeleteAsync(row.Id, CancellationToken.None);
        StatusText = row.IsRecurring
            ? $"Deleted the repeating {row.AmountText} entry."
            : $"Deleted {row.AmountText} from {row.ShortDateText}.";
        await ReloadAsync();
    }

    partial void OnShowRepeatingOnlyChanged(bool value) => RequestReload();

    partial void OnSearchTextChanged(string? value)
    {
        // Typing shouldn't hit the database on every keystroke.
        _searchDelay?.Cancel();
        _searchDelay?.Dispose();
        _searchDelay = new CancellationTokenSource();
        var token = _searchDelay.Token;
        _ = ReloadAfterSearchDelayAsync(token);
    }

    private async Task ReloadAfterSearchDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
            if (!token.IsCancellationRequested)
                Dispatcher.UIThread.Post(() => _ = ReloadAsync());
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = null;

    protected static string FormatDay(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);
}

/// <summary>Income section: salary, investment, interest, tips, and any custom type.</summary>
public sealed class IncomeViewModel(
    IEntryRepository entries,
    IEntryQueryService entryQueries,
    ICategoryRepository categories,
    IAccountRepository accounts,
    INavigationService navigation,
    IClock clock,
    ISettingsStore settings)
    : LedgerViewModel(
        EntryKind.Income, entries, entryQueries, categories, accounts, navigation, clock, settings)
{
    public override string Title => "Income";
    public override string AddButtonText => "＋ Add income";
    public override string EmptyStateText => "No income recorded in this range yet.";
}

/// <summary>Expenses section: rent, utilities, credit card, mortgage, fees, and custom categories.</summary>
public sealed class ExpensesViewModel(
    IEntryRepository entries,
    IEntryQueryService entryQueries,
    ICategoryRepository categories,
    IAccountRepository accounts,
    INavigationService navigation,
    IClock clock,
    ISettingsStore settings)
    : LedgerViewModel(
        EntryKind.Expense, entries, entryQueries, categories, accounts, navigation, clock, settings)
{
    public override string Title => "Expenses";
    public override string AddButtonText => "＋ Add expense";
    public override string EmptyStateText => "No expenses recorded in this range yet.";
}
