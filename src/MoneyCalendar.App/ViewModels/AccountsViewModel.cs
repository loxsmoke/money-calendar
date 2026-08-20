using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MoneyCalendar.App.Services;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.App.ViewModels;

/// <summary>One account row: name, type, masked digits, and the income it has received so far.</summary>
public sealed class AccountRowViewModel(Account account, decimal incomeToDate, string currencyCode)
    : ViewModelBase
{
    public Account Account { get; } = account;

    public Guid Id => Account.Id;
    public string Name => Account.Name;
    public string TypeText { get; } = AccountTypes.Label(account.Type);
    public IBrush TypeColor { get; } = new SolidColorBrush(Color.Parse(AccountTypes.ColorHex(account.Type)));
    public string DigitsText { get; } = string.IsNullOrWhiteSpace(account.Last4) ? "" : $"••••{account.Last4}";
    public string NoteText { get; } = account.Note ?? "";

    /// <summary>Income received into this account up to today — future-dated entries do not count.</summary>
    public decimal IncomeToDate { get; } = incomeToDate;
    public bool HasIncome { get; } = incomeToDate > 0m;
    public string IncomeToDateText { get; } =
        incomeToDate > 0m ? Format.Money(incomeToDate, currencyCode) : "—";
}

/// <summary>
/// What deleting an account would mean, and what can be done about it. An account is never
/// silently unlinked from its transactions: either it is unused and simply goes, or its
/// transactions move to another account of the same side first, or the delete is refused
/// because there is nowhere to move them to.
/// </summary>
public sealed record AccountDeletionPlan(
    AccountRowViewModel Account,
    int UsageCount,
    IReadOnlyList<AccountChoice> Replacements)
{
    public bool IsUsed => UsageCount > 0;

    /// <summary>Used, but nothing of the same side to move the transactions to.</summary>
    public bool IsBlocked => IsUsed && Replacements.Count == 0;

    public bool NeedsReassignment => IsUsed && Replacements.Count > 0;

    public string Title => IsBlocked ? "Cannot delete this account" : "Delete account";

    public string Message => (IsUsed, IsBlocked) switch
    {
        (false, _) =>
            $"Delete '{Account.Name}'? No transaction uses it, so nothing else changes.",
        (true, true) =>
            $"{Format.Count(UsageCount, "transaction uses", "transactions use")} '{Account.Name}', and there is no " +
            $"other {Account.TypeText.ToLowerInvariant()}-side account to move them to. Add one first, or delete " +
            "those transactions.",
        _ =>
            $"{Format.Count(UsageCount, "transaction uses", "transactions use")} '{Account.Name}'. They will be " +
            "moved to the account you pick, and then this one is deleted.",
    };
}

/// <summary>A section of the list: all accounts of one type, with their combined income.</summary>
public sealed record AccountGroup(
    string TypeText, IBrush Color, IReadOnlyList<AccountRowViewModel> Accounts, string IncomeToDateText)
{
    public string CountText { get; } = Format.Count(Accounts.Count, "account", "accounts");
}

/// <summary>
/// Accounts section: the list of accounts the user keeps, each with a name, a type, and the
/// income recorded against it so far. Expenses are not posted to an account yet, so the totals
/// here are receipts, not balances.
/// </summary>
public partial class AccountsViewModel(
    IAccountRepository accounts,
    IEntryQueryService entries,
    IClock clock,
    ISettingsStore settings) : PageViewModel
{
    public override string Title => "Accounts";

    public ObservableCollection<AccountRowViewModel> Rows { get; } = [];
    [ObservableProperty] private IReadOnlyList<AccountGroup> _groups = [];
    [ObservableProperty] private AccountRowViewModel? _selectedRow;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string _countText = "";

    protected override async Task<bool> LoadAsync(CancellationToken ct)
    {
        var all = await accounts.GetAllAsync(ct);
        var received = await IncomeByAccountAsync(ct);
        var currency = settings.Current.CurrencyCode;

        Rows.Clear();
        foreach (var account in all)
            Rows.Add(new AccountRowViewModel(account, received.GetValueOrDefault(account.Id), currency));

        Groups = Rows
            .GroupBy(r => r.Account.Type)
            .OrderBy(g => g.Key)
            .Select(g => new AccountGroup(
                AccountTypes.Label(g.Key),
                new SolidColorBrush(Color.Parse(AccountTypes.ColorHex(g.Key))),
                g.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList(),
                GroupIncomeText(g, currency)))
            .ToList();

        CountText = Format.Count(Rows.Count, "account", "accounts");
        return Rows.Count > 0;
    }

    /// <summary>
    /// Income per account, counted from the first entry ever recorded up to today. Repeating
    /// entries are expanded, so a monthly salary contributes every occurrence that has passed.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> IncomeByAccountAsync(CancellationToken ct)
    {
        var toDate = new DateRange(DateOnly.MinValue.AddDays(1), clock.Today);
        var income = await entries.GetAsync(new EntryFilter(Range: toDate, Kind: EntryKind.Income), ct);

        return income
            .Where(e => e.AccountId is not null)
            .GroupBy(e => e.AccountId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
    }

    private static string GroupIncomeText(IEnumerable<AccountRowViewModel> group, string currency)
    {
        var total = group.Sum(r => r.IncomeToDate);
        return total > 0m ? Format.Money(total, currency) : "";
    }

    /// <summary>Builds editor state for the dialog; the view owns the window, this owns the data.</summary>
    public AccountEditorViewModel CreateEditor(AccountRowViewModel? row) =>
        new(row?.Account, Rows.Select(r => r.Account).ToList());

    /// <summary>Commits a dialog result. Returns false when validation failed.</summary>
    public async Task<bool> SaveEditorAsync(AccountEditorViewModel editor)
    {
        if (editor.TryBuild() is not { } account)
            return false;

        if (editor.IsNew)
        {
            await accounts.AddAsync(account, CancellationToken.None);
            StatusText = $"Added {account.Name}.";
        }
        else
        {
            await accounts.UpdateAsync(account, CancellationToken.None);
            StatusText = $"Updated {account.Name}.";
        }

        await ReloadAsync();
        return true;
    }

    /// <summary>
    /// Works out what deleting this account would do, so the view can say it before doing it.
    /// Replacements are accounts on the same side — income transactions cannot be moved onto a
    /// credit card, and expense destinations cannot be moved onto a checking account.
    /// </summary>
    public async Task<AccountDeletionPlan> PrepareDeleteAsync(AccountRowViewModel row)
    {
        var usage = await accounts.UsageCountAsync(row.Id, CancellationToken.None);
        var sameSide = Rows
            .Where(r => r.Id != row.Id && AccountTypes.IsIncome(r.Account.Type) == AccountTypes.IsIncome(row.Account.Type))
            .Select(r => new AccountChoice(r.Id, r.Name))
            .ToList();
        return new AccountDeletionPlan(row, usage, sameSide);
    }

    /// <summary>
    /// Deletes the account, first moving its transactions to <paramref name="moveTo"/> when the
    /// plan calls for it. Refuses when the plan is blocked.
    /// </summary>
    public async Task<bool> DeleteAsync(AccountDeletionPlan plan, Guid? moveTo)
    {
        if (plan.IsBlocked)
            return false;

        if (plan.NeedsReassignment)
        {
            if (moveTo is not { } target)
                return false;

            var moved = await accounts.ReassignAsync(plan.Account.Id, target, CancellationToken.None);
            var name = plan.Replacements.First(r => r.Id == target).Label;
            StatusText = $"Moved {Format.Count(moved, "transaction", "transactions")} to {name}, " +
                $"then deleted {plan.Account.Name}.";
        }
        else
        {
            StatusText = $"Deleted {plan.Account.Name}.";
        }

        await accounts.DeleteAsync(plan.Account.Id, CancellationToken.None);
        await ReloadAsync();
        return true;
    }
}
