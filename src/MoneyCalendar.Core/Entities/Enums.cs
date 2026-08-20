namespace MoneyCalendar.Core.Entities;

/// <summary>
/// Every entry is either money in or money out. The prototype has no transfers and no
/// accounts, so this single flag decides sign, color, and which section owns the row.
/// </summary>
public enum EntryKind
{
    Income = 0,
    Expense = 1,
}

/// <summary>Bucket width used to group a range into chart columns.</summary>
public enum BucketSize
{
    Day = 0,
    Week = 1,
}

/// <summary>What kind of account money sits in or flows through.</summary>
public enum AccountType
{
    Credit = 0,
    Checking = 1,
    Savings = 2,
    Investment = 3,
    Mortgage = 4,
    OtherIncome = 5,
    OtherExpense = 6,
}

/// <summary>How often a repeating entry comes back. <see cref="None"/> is a one-off.</summary>
public enum RecurrenceFrequency
{
    None = 0,
    Weekly = 1,
    BiWeekly = 2,
    TwiceMonthly = 3,
    Monthly = 4,
}

/// <summary>
/// How the second day of a twice-monthly entry is chosen: a fixed day, the middle of the
/// month, or whatever the last day happens to be.
/// </summary>
public enum MonthDayMode
{
    OnDay = 0,
    MidMonth = 1,
    LastDay = 2,
}
