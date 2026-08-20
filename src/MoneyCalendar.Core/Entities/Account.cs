namespace MoneyCalendar.Core.Entities;

/// <summary>
/// An account the user keeps track of: a card, a bank account, an investment pot, a mortgage.
/// The prototype uses these as a named list — entries are not posted to an account yet.
/// </summary>
public class Account
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public AccountType Type { get; set; }

    /// <summary>Last digits of the account number, e.g. "4417". Never a full number.</summary>
    public string? Last4 { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Display names for <see cref="AccountType"/>, kept out of the UI layer so exports match.</summary>
public static class AccountTypes
{
    public static IReadOnlyList<AccountType> All { get; } =
    [
        AccountType.Credit,
        AccountType.Checking,
        AccountType.Savings,
        AccountType.Investment,
        AccountType.Mortgage,
        AccountType.OtherIncome,
        AccountType.OtherExpense,
    ];

    /// <summary>
    /// Accounts money comes into. Everything else — credit, mortgage, other expense — is where
    /// money goes out, so income can only be posted to one of these.
    /// </summary>
    public static IReadOnlyList<AccountType> IncomeTypes { get; } =
    [
        AccountType.Checking,
        AccountType.Savings,
        AccountType.Investment,
        AccountType.OtherIncome,
    ];

    public static bool IsIncome(AccountType type) => IncomeTypes.Contains(type);

    public static string Label(AccountType type) => type switch
    {
        AccountType.Credit => "Credit",
        AccountType.Checking => "Checking",
        AccountType.Savings => "Savings",
        AccountType.Investment => "Investment",
        AccountType.Mortgage => "Mortgage",
        AccountType.OtherIncome => "Other income",
        AccountType.OtherExpense => "Other expense",
        _ => type.ToString(),
    };

    public static string ColorHex(AccountType type) => type switch
    {
        AccountType.Credit => "#8E44AD",
        AccountType.Checking => "#4A6FA5",
        AccountType.Savings => "#3F7D5C",
        AccountType.Investment => "#2E7D6F",
        AccountType.Mortgage => "#B3541E",
        AccountType.OtherIncome => "#4C9A2A",
        _ => "#8A8A92",
    };
}
