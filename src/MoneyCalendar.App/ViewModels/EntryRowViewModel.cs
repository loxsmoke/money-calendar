using Avalonia.Media;
using MoneyCalendar.App.Services;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Services;

namespace MoneyCalendar.App.ViewModels;

/// <summary>One row in a transaction list. Read-only projection — edits go through the dialog.</summary>
public sealed class EntryRowViewModel(Entry entry, Category? category, string currencyCode) : ViewModelBase
{
    public Entry Entry { get; } = entry;

    public Guid Id => Entry.Id;
    public DateOnly Date => Entry.Date;
    public EntryKind Kind => Entry.Kind;
    public bool IsIncome => Entry.Kind == EntryKind.Income;

    public string DateText { get; } = Format.MediumDate(entry.Date);

    /// <summary>
    /// What the Amount column sorts by. The displayed text is formatted and signed, so sorting
    /// the column by its string would order "-$9.00" next to "-$90.00"; this is the number.
    /// </summary>
    public decimal SortAmount => Entry.SignedAmount;
    public string ShortDateText { get; } = Format.ShortDate(entry.Date);
    public string CategoryName { get; } = category?.Name ?? "Uncategorized";
    public IBrush CategoryColor { get; } = new SolidColorBrush(Color.Parse(category?.ColorHex ?? "#8A8A92"));
    public string KindText { get; } = entry.Kind == EntryKind.Income ? "Income" : "Expense";
    public string AmountText { get; } = Format.Money(
        entry.Kind == EntryKind.Income ? entry.Amount : -entry.Amount, currencyCode, explicitSign: true);

    /// <summary>
    /// Income shows the account it landed in; an expense shows the flow, "Checking → Visa".
    /// Entries recorded before accounts existed fall back to their free-text card fields.
    /// </summary>
    public string? AccountText { get; } = AccountFlow(entry);

    public bool HasAccount => AccountText is not null;
    public string NoteText { get; } = entry.Note ?? "";

    /// <summary>Projected occurrences and their template both carry the pattern description.</summary>
    public bool IsRecurring { get; } = entry.IsRecurring;
    public string RepeatText { get; } = entry.IsRecurring ? RecurrenceExpander.Describe(entry) : "";

    private static string? AccountFlow(Entry entry)
    {
        // With both ends shown the row gets long, so only the destination keeps its digits —
        // that is the card or loan the payment is identified by.
        var both = entry.Account is not null && entry.ToAccount is not null;
        var from = entry.Account is { } account
            ? both ? account.Name : Format.Account(account.Name, account.Last4)
            : null;
        var to = entry.ToAccount is { } toAccount ? Format.Account(toAccount.Name, toAccount.Last4) : null;

        return (from, to) switch
        {
            (not null, not null) => $"{from} → {to}",
            (not null, null) => from,
            (null, not null) => to,
            _ => Format.Account(entry.AccountName, entry.AccountLast4),
        };
    }
}
