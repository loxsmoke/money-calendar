using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Seed;

/// <summary>
/// The demo ledger a fresh install starts with, and what Settings → "Load sample data" adds.
///
/// It is a planning ledger rather than a history: a payroll and an interest payment coming in,
/// the standing bills going out, and a few one-offs still ahead. Every id here is fixed, so
/// loading it twice adds nothing the second time — the repositories skip ids they already hold.
///
/// Dates are anchored to <c>today</c>, not to the calendar: the repeating series start on the
/// first of last month so there is a month of history behind the current one, and the one-offs
/// sit in the days and months ahead.
/// </summary>
public static class SampleData
{
    private static Guid Id(string group, int index) =>
        Guid.Parse($"0000000{group}-0000-4000-8000-{index:D12}");

    // ---- accounts ---------------------------------------------------------

    public static Guid Checking { get; } = Id("b", 1);
    public static Guid Savings { get; } = Id("b", 2);
    public static Guid Visa { get; } = Id("b", 3);
    public static Guid Mastercard { get; } = Id("b", 4);
    public static Guid Mortgage { get; } = Id("b", 5);
    public static Guid BillPayments { get; } = Id("b", 6);

    /// <summary>The accounts the sample ledger files against.</summary>
    public static IReadOnlyList<Account> BuildAccounts(DateTimeOffset stamp) =>
    [
        new() { Id = Checking, Name = "Demo Checking", Type = AccountType.Checking, CreatedAt = stamp, UpdatedAt = stamp },
        new() { Id = Savings, Name = "Demo Savings", Type = AccountType.Savings, CreatedAt = stamp, UpdatedAt = stamp },
        new() { Id = Visa, Name = "Demo Visa", Type = AccountType.Credit, Last4 = "1111", CreatedAt = stamp, UpdatedAt = stamp },
        new() { Id = Mastercard, Name = "Demo Mastercard", Type = AccountType.Credit, Last4 = "2222", CreatedAt = stamp, UpdatedAt = stamp },
        new() { Id = Mortgage, Name = "Demo Mortgage", Type = AccountType.Mortgage, CreatedAt = stamp, UpdatedAt = stamp },
        new() { Id = BillPayments, Name = "Bill payments", Type = AccountType.OtherExpense, CreatedAt = stamp, UpdatedAt = stamp },
    ];

    // ---- categories -------------------------------------------------------

    public static Guid Internet { get; } = Id("c", 1);
    public static Guid CellPhone { get; } = Id("c", 2);
    public static Guid CarPayment { get; } = Id("c", 3);

    /// <summary>
    /// The categories the sample ledger needs beyond the built-in ones. They are ordinary custom
    /// categories: deletable, and shown in the custom panel in Settings like any other.
    /// </summary>
    public static IReadOnlyList<Category> BuildCategories() =>
    [
        new() { Id = Internet, Name = "Internet", Kind = EntryKind.Expense, ColorHex = "#4FA3A5", SortOrder = 500 },
        new() { Id = CellPhone, Name = "Cell phone", Kind = EntryKind.Expense, ColorHex = "#C9A227", SortOrder = 500 },
        new() { Id = CarPayment, Name = "Car payment", Kind = EntryKind.Expense, ColorHex = "#6C7BD1", SortOrder = 500 },
    ];

    // ---- entries ----------------------------------------------------------

    /// <summary>
    /// The demo ledger, dated relative to <paramref name="today"/>. When
    /// <paramref name="accounts"/> is supplied, entries are wired to whichever of them match by
    /// name, so a ledger seeded once keeps its links when the sample is loaded again.
    /// </summary>
    public static IReadOnlyList<Entry> Build(
        DateOnly today,
        string currencyCode,
        DateTimeOffset stamp,
        IReadOnlyList<Account>? accounts = null)
    {
        var lastMonth = today.AddMonths(-1);
        var seriesStart = new DateOnly(lastMonth.Year, lastMonth.Month, 1);
        var entries = new List<Entry>();

        // Match the account by the name this sample gives it, falling back to the first of its
        // type — a ledger whose accounts were renamed still gets its entries filed somewhere.
        Guid? Account(Guid id, AccountType type)
        {
            if (accounts is null)
                return id;
            var named = BuildAccounts(stamp).First(a => a.Id == id).Name;
            return accounts.FirstOrDefault(a => string.Equals(a.Name, named, StringComparison.OrdinalIgnoreCase))?.Id
                ?? accounts.FirstOrDefault(a => a.Type == type)?.Id;
        }

        void Add(
            int index,
            DateOnly date,
            decimal amount,
            EntryKind kind,
            Guid categoryId,
            Guid? toAccount = null,
            AccountType toType = AccountType.OtherExpense,
            RecurrenceFrequency frequency = RecurrenceFrequency.None,
            int? dayOfMonth = null,
            int? secondDayOfMonth = null) =>
            entries.Add(new Entry
            {
                Id = Id("d", index),
                Date = date,
                Amount = amount,
                Kind = kind,
                CategoryId = categoryId,
                // Everything runs through the one checking account: income lands in it,
                // expenses are paid from it.
                AccountId = Account(Checking, AccountType.Checking),
                ToAccountId = kind == EntryKind.Expense && toAccount is { } to ? Account(to, toType) : null,
                CurrencyCode = currencyCode,
                Frequency = frequency,
                DayOfMonth = dayOfMonth,
                SecondDayOfMonth = secondDayOfMonth,
                CreatedAt = stamp,
                UpdatedAt = stamp,
            });

        // ---- what comes in, every month ----
        Add(1, seriesStart, 2000m, EntryKind.Income, DefaultCategories.Salary,
            frequency: RecurrenceFrequency.TwiceMonthly, dayOfMonth: 1, secondDayOfMonth: 15);
        Add(2, seriesStart, 150m, EntryKind.Income, DefaultCategories.Interest,
            frequency: RecurrenceFrequency.Monthly, dayOfMonth: 5);

        // ---- the standing bills ----
        Add(3, seriesStart, 1000m, EntryKind.Expense, DefaultCategories.Mortgage,
            Mortgage, AccountType.Mortgage, RecurrenceFrequency.Monthly, dayOfMonth: 2);
        Add(4, seriesStart, 600m, EntryKind.Expense, CarPayment,
            BillPayments, AccountType.OtherExpense, RecurrenceFrequency.Monthly, dayOfMonth: 6);
        Add(5, seriesStart, 80m, EntryKind.Expense, DefaultCategories.Utilities,
            BillPayments, AccountType.OtherExpense, RecurrenceFrequency.Monthly, dayOfMonth: 10);
        Add(6, seriesStart, 50m, EntryKind.Expense, Internet,
            BillPayments, AccountType.OtherExpense, RecurrenceFrequency.Monthly, dayOfMonth: 11);
        Add(7, seriesStart, 45m, EntryKind.Expense, CellPhone,
            BillPayments, AccountType.OtherExpense, RecurrenceFrequency.Monthly, dayOfMonth: 17);

        // ---- one-offs still ahead, which is what the balance line is for ----
        Add(8, today.AddDays(2), 1200m, EntryKind.Expense, DefaultCategories.CreditCard,
            Mastercard, AccountType.Credit);
        Add(9, today.AddDays(3), 1500m, EntryKind.Expense, DefaultCategories.CreditCard,
            Visa, AccountType.Credit);
        Add(10, OnDayOfMonth(today.AddMonths(1), 17), 1300m, EntryKind.Expense, DefaultCategories.Rent,
            Mastercard, AccountType.Credit);
        Add(11, OnDayOfMonth(today.AddMonths(2), 13), 1400m, EntryKind.Expense, DefaultCategories.Rent,
            Mastercard, AccountType.Credit);

        return entries.OrderBy(e => e.Date).ThenBy(e => e.Kind).ToList();
    }

    /// <summary>A day inside the given month, pulled back to its last day in a short month.</summary>
    private static DateOnly OnDayOfMonth(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
}
