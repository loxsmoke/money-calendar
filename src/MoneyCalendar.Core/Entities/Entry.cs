namespace MoneyCalendar.Core.Entities;

/// <summary>
/// One dated income or expense.
///
/// Amount convention: always stored as a positive magnitude; <see cref="Kind"/> carries the
/// direction. Summaries negate expenses when they need a signed net figure. (The budget app
/// stores signed amounts because providers deliver them that way; here every row is
/// hand-entered from the calendar, so a magnitude plus a kind is less error-prone.)
///
/// A row with <see cref="Frequency"/> set is not a single dated entry but the template of a
/// repeating series: <see cref="Date"/> is when the series starts, and the pattern fields say
/// which days it lands on. Reads go through the entry query service, which expands templates
/// into the occurrences that fall inside the range being asked about.
/// </summary>
public class Entry
{
    public Guid Id { get; set; }

    /// <summary>The entry's date, or the first day of the series when this is a template.</summary>
    public DateOnly Date { get; set; }

    public decimal Amount { get; set; }
    public EntryKind Kind { get; set; }
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>
    /// The money account this entry touches: for income, the account it lands in; for an
    /// expense, the account it is paid from. Always one of the income account types.
    /// </summary>
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>
    /// Expenses only: the account the money goes to — a card, a mortgage, or another expense
    /// account. Income has no counterparty account, so this stays null.
    /// </summary>
    public Guid? ToAccountId { get; set; }
    public Account? ToAccount { get; set; }

    /// <summary>ISO 4217.</summary>
    public required string CurrencyCode { get; set; }

    /// <summary>Card or account nickname typed by hand, e.g. "Chase Sapphire".</summary>
    public string? AccountName { get; set; }

    /// <summary>Last digits of the account number, e.g. "4417". Never a full number.</summary>
    public string? AccountLast4 { get; set; }

    public string? Note { get; set; }

    // ---- recurrence ------------------------------------------------------

    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>Monthly: the day it lands on. Twice monthly: the first of the two days. 1–31.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>Twice monthly: the second day, when <see cref="SecondDayMode"/> is OnDay. 1–31.</summary>
    public int? SecondDayOfMonth { get; set; }

    /// <summary>Twice monthly: whether the second day is a fixed day, mid month, or the last day.</summary>
    public MonthDayMode SecondDayMode { get; set; }

    /// <summary>Weekly and bi-weekly: the weekday it lands on.</summary>
    public DayOfWeek? Weekday { get; set; }

    /// <summary>
    /// The last day the series may land on. Null means it runs on indefinitely.
    /// </summary>
    public DateOnly? RecurrenceEnd { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public decimal SignedAmount => Kind == EntryKind.Income ? Amount : -Amount;

    public bool IsRecurring => Frequency != RecurrenceFrequency.None;

    /// <summary>
    /// True on the throwaway copies the query service produces for a series. They are never
    /// tracked or saved; editing or deleting one acts on the template it came from.
    /// </summary>
    public bool IsOccurrence { get; set; }

    /// <summary>A projected occurrence of this template, dated <paramref name="date"/>.</summary>
    public Entry OccurrenceOn(DateOnly date) => new()
    {
        Id = Id,
        Date = date,
        Amount = Amount,
        Kind = Kind,
        CategoryId = CategoryId,
        Category = Category,
        AccountId = AccountId,
        Account = Account,
        ToAccountId = ToAccountId,
        ToAccount = ToAccount,
        CurrencyCode = CurrencyCode,
        AccountName = AccountName,
        AccountLast4 = AccountLast4,
        Note = Note,
        Frequency = Frequency,
        DayOfMonth = DayOfMonth,
        SecondDayOfMonth = SecondDayOfMonth,
        SecondDayMode = SecondDayMode,
        Weekday = Weekday,
        RecurrenceEnd = RecurrenceEnd,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        IsOccurrence = true,
    };
}
