using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Services;

namespace MoneyCalendar.ViewModels;

/// <summary>A category as offered by the editor's picker.</summary>
public sealed record CategoryChoice(Guid Id, string Name, bool WantsAccountDetails)
{
    public override string ToString() => Name;
}

/// <summary>An account as offered by the editor's picker.</summary>
public sealed record AccountChoice(Guid Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>How often the entry repeats, as offered by the editor's picker.</summary>
public sealed record FrequencyChoice(RecurrenceFrequency Frequency, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A day-of-month choice: a plain day, or "Mid month" / "Last day of month".</summary>
public sealed record MonthDayChoice(int Day, MonthDayMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A weekday choice for weekly and bi-weekly entries.</summary>
public sealed record WeekdayChoice(DayOfWeek Day, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Add/edit dialog state for a single entry. The dialog validates on save and reports the
/// first problem inline; nothing touches the repository until the caller commits.
///
/// Money always flows between accounts: income lands in an income account, an expense is paid
/// from an income account to an expense account. Any entry can also be marked as repeating,
/// which reveals the pattern its occurrences follow.
/// </summary>
public partial class EntryEditorViewModel : ViewModelBase
{
    private readonly Entry? _original;

    public EntryEditorViewModel(
        EntryKind kind,
        IReadOnlyList<CategoryChoice> categories,
        IReadOnlyList<AccountChoice> incomeAccounts,
        IReadOnlyList<AccountChoice> expenseAccounts,
        string currencyCode,
        DateOnly defaultDate,
        Entry? existing = null)
    {
        Kind = kind;
        Categories = categories;
        Accounts = incomeAccounts;
        ToAccounts = expenseAccounts;
        CurrencyCode = currencyCode;
        _original = existing;

        var isNew = existing is null;
        Title = (isNew, kind) switch
        {
            (true, EntryKind.Income) => "Add income",
            (true, EntryKind.Expense) => "Add expense",
            (false, EntryKind.Income) => "Edit income",
            _ => "Edit expense",
        };
        CategoryLabel = kind == EntryKind.Income ? "Type" : "Category";

        var startDate = existing?.Date ?? defaultDate;
        _date = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        _amountText = existing is null
            ? ""
            : existing.Amount.ToString("0.##", CultureInfo.CurrentCulture);
        _selectedCategory = existing is not null
            ? categories.FirstOrDefault(c => c.Id == existing.CategoryId) ?? categories.FirstOrDefault()
            : categories.FirstOrDefault();
        // A new entry starts on the first account; an existing one whose account is gone starts
        // blank, so saving it has to pick a real account.
        _selectedAccount = incomeAccounts.FirstOrDefault(a => a.Id == existing?.AccountId)
            ?? (isNew ? incomeAccounts.FirstOrDefault() : null);
        _selectedToAccount = expenseAccounts.FirstOrDefault(a => a.Id == existing?.ToAccountId)
            ?? (isNew ? expenseAccounts.FirstOrDefault() : null);
        _accountName = existing?.AccountName ?? "";
        _accountLast4 = existing?.AccountLast4 ?? "";
        _note = existing?.Note ?? "";

        _repeats = existing?.IsRecurring ?? false;
        _selectedFrequency = Frequencies.FirstOrDefault(f => f.Frequency == existing?.Frequency)
            ?? Frequencies[0];
        // A brand-new series defaults to the 1st; an existing one keeps the day it was saved
        // with, falling back to the day its start date lands on.
        var monthDay = existing?.DayOfMonth ?? (isNew ? 1 : startDate.Day);
        _selectedDayOfMonth = MonthDays.FirstOrDefault(d => d.Day == monthDay) ?? MonthDays[0];
        _selectedSecondDay = SecondMonthDays.FirstOrDefault(d =>
                d.Mode == existing?.SecondDayMode
                && (d.Mode != MonthDayMode.OnDay || d.Day == existing.SecondDayOfMonth))
            ?? SecondMonthDays.First(d => d.Mode == MonthDayMode.LastDay);
        _selectedWeekday = Weekdays.FirstOrDefault(w => w.Day == (existing?.Weekday ?? startDate.DayOfWeek))
            ?? Weekdays[0];
        _ends = existing?.RecurrenceEnd is not null;
        _endDate = new DateTimeOffset(
            (existing?.RecurrenceEnd ?? startDate.AddYears(1)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    public EntryKind Kind { get; }
    public IReadOnlyList<CategoryChoice> Categories { get; }
    /// <summary>Income accounts: where income lands, and where an expense is paid from.</summary>
    public IReadOnlyList<AccountChoice> Accounts { get; }

    /// <summary>Expense accounts: where an expense goes to.</summary>
    public IReadOnlyList<AccountChoice> ToAccounts { get; }
    public string CurrencyCode { get; }
    public string Title { get; }
    public string CategoryLabel { get; }
    public bool IsNew => _original is null;

    /// <summary>Income lands in an account; an expense comes out of one.</summary>
    public string AccountLabel => Kind == EntryKind.Income ? "Into account" : "From account";

    public bool ShowAccountPicker => true;

    /// <summary>Only an expense has a counterparty account.</summary>
    public bool ShowToAccountPicker => Kind == EntryKind.Expense;

    /// <summary>False when no income account exists yet — the entry cannot be saved until one does.</summary>
    public bool HasAccounts => Accounts.Count > 0;

    public bool HasToAccounts => ToAccounts.Count > 0;

    public string NoAccountsText =>
        "Add a checking, savings, investment or other-income account in the Accounts section first.";

    public string NoToAccountsText =>
        "Add a credit, mortgage or other-expense account in the Accounts section first.";

    public IReadOnlyList<FrequencyChoice> Frequencies { get; } =
    [
        new(RecurrenceFrequency.Monthly, "Monthly"),
        new(RecurrenceFrequency.TwiceMonthly, "Twice monthly"),
        new(RecurrenceFrequency.BiWeekly, "Every 2 weeks"),
        new(RecurrenceFrequency.Weekly, "Weekly"),
    ];

    /// <summary>Plain days 1–31, for the monthly day and the first twice-monthly day.</summary>
    public IReadOnlyList<MonthDayChoice> MonthDays { get; } =
        [.. Enumerable.Range(1, 31).Select(d =>
            new MonthDayChoice(d, MonthDayMode.OnDay, RecurrenceExpander.Ordinal(d)))];

    /// <summary>The second twice-monthly day: any day, or the two relative options.</summary>
    public IReadOnlyList<MonthDayChoice> SecondMonthDays { get; } =
    [
        new(15, MonthDayMode.MidMonth, "Mid month (15th)"),
        new(0, MonthDayMode.LastDay, "Last day of month"),
        .. Enumerable.Range(1, 31).Select(d =>
            new MonthDayChoice(d, MonthDayMode.OnDay, RecurrenceExpander.Ordinal(d))),
    ];

    public IReadOnlyList<WeekdayChoice> Weekdays { get; } =
        [.. Enumerable.Range(0, 7).Select(i =>
        {
            var day = (DayOfWeek)(((int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + i) % 7);
            return new WeekdayChoice(day, CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day));
        })];

    [ObservableProperty] private DateTimeOffset _date;
    [ObservableProperty] private string _amountText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAccountFields))]
    private CategoryChoice? _selectedCategory;

    [ObservableProperty] private AccountChoice? _selectedAccount;
    [ObservableProperty] private AccountChoice? _selectedToAccount;
    [ObservableProperty] private string _accountName;
    [ObservableProperty] private string _accountLast4;
    [ObservableProperty] private string _note;
    [ObservableProperty] private string? _errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(DateLabel), nameof(RepeatSummary),
        nameof(ShowDayOfMonth), nameof(ShowSecondDay), nameof(ShowWeekday))]
    private bool _repeats;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowDayOfMonth), nameof(ShowSecondDay), nameof(ShowWeekday), nameof(RepeatSummary))]
    private FrequencyChoice _selectedFrequency;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatSummary))]
    private MonthDayChoice _selectedDayOfMonth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatSummary))]
    private MonthDayChoice _selectedSecondDay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatSummary))]
    private WeekdayChoice _selectedWeekday;

    /// <summary>Whether the series stops on <see cref="EndDate"/> rather than running on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatSummary))]
    private bool _ends;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatSummary))]
    private DateTimeOffset _endDate;

    /// <summary>Credit card / mortgage style categories ask for the account name and last digits.</summary>
    public bool ShowAccountFields => SelectedCategory?.WantsAccountDetails == true;

    /// <summary>A repeating entry's date is where the series begins, not a one-off date.</summary>
    public string DateLabel => Repeats ? "Starts on" : "Date";

    public string FirstDayLabel =>
        SelectedFrequency.Frequency == RecurrenceFrequency.TwiceMonthly ? "First day" : "Day of month";

    public bool ShowDayOfMonth => Repeats && SelectedFrequency.Frequency
        is RecurrenceFrequency.Monthly or RecurrenceFrequency.TwiceMonthly;

    public bool ShowSecondDay => Repeats && SelectedFrequency.Frequency == RecurrenceFrequency.TwiceMonthly;

    public bool ShowWeekday => Repeats && SelectedFrequency.Frequency
        is RecurrenceFrequency.Weekly or RecurrenceFrequency.BiWeekly;

    /// <summary>Plain-language echo of the pattern, so the dialog says what it will produce.</summary>
    public string RepeatSummary => Repeats ? RecurrenceExpander.Describe(BuildPattern(new Entry
    {
        CurrencyCode = CurrencyCode,
        Date = DateOnly.FromDateTime(Date.Date),
    })) : "";

    /// <summary>Validates and produces the entity to persist, or null with <see cref="ErrorText"/> set.</summary>
    public Entry? TryBuild()
    {
        if (SelectedCategory is null)
        {
            ErrorText = "Pick a category first.";
            return null;
        }

        // Money always moves between real accounts, so both ends are required.
        if (SelectedAccount is null)
        {
            ErrorText = HasAccounts
                ? Kind == EntryKind.Income
                    ? "Pick the account this income goes into."
                    : "Pick the account this expense is paid from."
                : NoAccountsText;
            return null;
        }

        if (Kind == EntryKind.Expense && SelectedToAccount is null)
        {
            ErrorText = HasToAccounts ? "Pick the account this expense goes to." : NoToAccountsText;
            return null;
        }

        if (!decimal.TryParse(AmountText?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            && !decimal.TryParse(AmountText?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            ErrorText = "Enter an amount, for example 125.50.";
            return null;
        }

        amount = Math.Abs(decimal.Round(amount, 2, MidpointRounding.AwayFromZero));
        if (amount == 0m)
        {
            ErrorText = "The amount has to be greater than zero.";
            return null;
        }

        var last4 = AccountLast4?.Trim() ?? "";
        if (last4.Length > 0 && (last4.Length > 4 || !last4.All(char.IsDigit)))
        {
            ErrorText = "Last digits must be up to 4 numbers, e.g. 4417.";
            return null;
        }

        if (Repeats && Ends && DateOnly.FromDateTime(EndDate.Date) < DateOnly.FromDateTime(Date.Date))
        {
            ErrorText = "The series cannot end before it starts.";
            return null;
        }

        if (Repeats && SelectedFrequency.Frequency == RecurrenceFrequency.TwiceMonthly
            && SelectedSecondDay.Mode == MonthDayMode.OnDay
            && SelectedSecondDay.Day == SelectedDayOfMonth.Day)
        {
            ErrorText = "The two monthly days have to differ.";
            return null;
        }

        ErrorText = null;
        var entry = new Entry
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Date = DateOnly.FromDateTime(Date.Date),
            Amount = amount,
            Kind = Kind,
            CategoryId = SelectedCategory.Id,
            AccountId = SelectedAccount!.Id,
            ToAccountId = Kind == EntryKind.Expense ? SelectedToAccount!.Id : null,
            CurrencyCode = CurrencyCode,
            AccountName = Trimmed(AccountName),
            AccountLast4 = last4.Length == 0 ? null : last4,
            Note = Trimmed(Note),
            CreatedAt = _original?.CreatedAt ?? default,
        };
        return BuildPattern(entry);
    }

    /// <summary>Copies the repeat selection onto an entry, or clears it when it does not repeat.</summary>
    private Entry BuildPattern(Entry entry)
    {
        if (!Repeats)
        {
            entry.Frequency = RecurrenceFrequency.None;
            entry.DayOfMonth = null;
            entry.SecondDayOfMonth = null;
            entry.SecondDayMode = MonthDayMode.OnDay;
            entry.Weekday = null;
            entry.RecurrenceEnd = null;
            return entry;
        }

        entry.Frequency = SelectedFrequency.Frequency;
        entry.DayOfMonth = ShowDayOfMonth ? SelectedDayOfMonth.Day : null;
        entry.SecondDayOfMonth = ShowSecondDay && SelectedSecondDay.Mode == MonthDayMode.OnDay
            ? SelectedSecondDay.Day
            : null;
        entry.SecondDayMode = ShowSecondDay ? SelectedSecondDay.Mode : MonthDayMode.OnDay;
        entry.Weekday = ShowWeekday ? SelectedWeekday.Day : null;
        entry.RecurrenceEnd = Ends ? DateOnly.FromDateTime(EndDate.Date) : null;
        return entry;
    }

    partial void OnSelectedFrequencyChanged(FrequencyChoice value) => OnPropertyChanged(nameof(FirstDayLabel));

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
