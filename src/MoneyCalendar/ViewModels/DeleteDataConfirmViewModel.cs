using CommunityToolkit.Mvvm.ComponentModel;
using MoneyCalendar.Services;

namespace MoneyCalendar.ViewModels;

/// <summary>How far a delete reaches, which is the only thing separating the two red buttons.</summary>
public enum DeleteScope
{
    /// <summary>Every stored transaction. Accounts, categories and settings stay.</summary>
    Transactions,

    /// <summary>Every transaction and every account. Categories and settings stay.</summary>
    Everything,
}

/// <summary>
/// The typed confirmation behind the two destructive buttons. Wiping a ledger is not undoable and
/// there is no trash to fish it back out of, so the button stays dead until the word is typed out.
/// </summary>
public partial class DeleteDataConfirmViewModel(
    int transactionCount,
    DeleteScope scope = DeleteScope.Transactions,
    int accountCount = 0,
    bool hasBackupAdvice = true)
    : ViewModelBase
{
    /// <summary>The word the user has to type. Compared case-insensitively.</summary>
    public const string RequiredWord = "delete";

    public int TransactionCount { get; } = transactionCount;
    public int AccountCount { get; } = accountCount;
    public DeleteScope Scope { get; } = scope;

    /// <summary>Names both the window and the button that commits, so the two never drift apart.</summary>
    public string Heading =>
        Scope == DeleteScope.Everything ? "Delete all data" : "Delete all transactions";

    public string Warning =>
        Scope == DeleteScope.Everything
            ? $"This deletes all {Format.Count(TransactionCount, "transaction", "transactions")} — including " +
              $"every repeating series — and all {Format.Count(AccountCount, "account", "accounts")}. " +
              "It cannot be undone."
            : $"This deletes all {Format.Count(TransactionCount, "transaction", "transactions")}, including every " +
              "repeating series. It cannot be undone.";

    /// <summary>What survives, so the label is not read wider than it is.</summary>
    public string ScopeText =>
        Scope == DeleteScope.Everything
            ? "Categories and settings are kept."
            : "Accounts, categories and settings are kept.";

    public string BackupAdvice => hasBackupAdvice
        ? "Export a JSON backup first if there is any chance you will want this data again."
        : "";

    public string Prompt => $"Type '{RequiredWord}' to confirm.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private string _typed = "";

    public bool CanDelete =>
        string.Equals(Typed?.Trim(), RequiredWord, StringComparison.OrdinalIgnoreCase);
}
