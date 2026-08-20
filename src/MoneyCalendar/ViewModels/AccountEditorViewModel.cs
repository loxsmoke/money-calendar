using CommunityToolkit.Mvvm.ComponentModel;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.ViewModels;

/// <summary>An account type as offered by the editor's picker.</summary>
public sealed record AccountTypeChoice(AccountType Type, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Add/edit dialog state for one account. Names have to stay unique, since they are how an
/// account is recognised everywhere else.
/// </summary>
public partial class AccountEditorViewModel : ViewModelBase
{
    private readonly Account? _original;
    private readonly IReadOnlyList<Account> _existing;

    public AccountEditorViewModel(Account? existing, IReadOnlyList<Account> allAccounts)
    {
        _original = existing;
        _existing = allAccounts;

        Title = existing is null ? "Add account" : "Edit account";
        Types = AccountTypes.All.Select(t => new AccountTypeChoice(t, AccountTypes.Label(t))).ToList();

        _name = existing?.Name ?? "";
        _selectedType = Types.FirstOrDefault(t => t.Type == (existing?.Type ?? AccountType.Checking))
            ?? Types[0];
        _last4 = existing?.Last4 ?? "";
        _note = existing?.Note ?? "";
    }

    public string Title { get; }
    public IReadOnlyList<AccountTypeChoice> Types { get; }
    public bool IsNew => _original is null;

    [ObservableProperty] private string _name;
    [ObservableProperty] private AccountTypeChoice _selectedType;
    [ObservableProperty] private string _last4;
    [ObservableProperty] private string _note;
    [ObservableProperty] private string? _errorText;

    /// <summary>Validates and produces the entity to persist, or null with <see cref="ErrorText"/> set.</summary>
    public Account? TryBuild()
    {
        var name = Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            ErrorText = "Give the account a name.";
            return null;
        }

        if (_existing.Any(a => a.Id != _original?.Id
            && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = $"'{name}' already exists.";
            return null;
        }

        var last4 = Last4?.Trim() ?? "";
        if (last4.Length > 0 && (last4.Length > 4 || !last4.All(char.IsDigit)))
        {
            ErrorText = "Last digits must be up to 4 numbers, e.g. 4417.";
            return null;
        }

        ErrorText = null;
        return new Account
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = name,
            Type = SelectedType.Type,
            Last4 = last4.Length == 0 ? null : last4,
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            CreatedAt = _original?.CreatedAt ?? default,
        };
    }
}
