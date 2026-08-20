using Microsoft.Extensions.DependencyInjection;
using MoneyCalendar.App.ViewModels;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.App.Services;

public enum PageKey
{
    Summary = 0,
    Income = 1,
    Expenses = 2,
    Accounts = 3,
    Settings = 4,
    About = 5,
}

/// <summary>Shell navigation plus the one deep link the app needs (calendar day → ledger).</summary>
public interface INavigationService
{
    PageViewModel Current { get; }
    PageKey CurrentKey { get; }
    event Action? CurrentChanged;

    void NavigateTo(PageKey key);

    /// <summary>Opens Income or Expenses scoped to a single day picked in the Summary calendar.</summary>
    void NavigateToLedger(EntryKind kind, DateRange range);

    /// <summary>Re-reads every cached page — used after an import or a data wipe.</summary>
    Task ReloadAllAsync();
}

/// <summary>
/// Pages are resolved lazily from DI and cached, so range selections and the calendar month
/// survive navigation. Navigating always kicks off a background reload; PageViewModel keeps
/// repeat loads from blanking the view.
/// </summary>
public sealed class NavigationService(IServiceProvider services) : INavigationService
{
    private readonly Dictionary<PageKey, PageViewModel> _pages = [];
    private PageViewModel? _current;

    public PageKey CurrentKey { get; private set; } = PageKey.Summary;
    public PageViewModel Current => _current ??= Resolve(PageKey.Summary);
    public event Action? CurrentChanged;

    public void NavigateTo(PageKey key)
    {
        var page = Resolve(key);
        CurrentKey = key;
        _current = page;
        _ = page.ReloadAsync();
        CurrentChanged?.Invoke();
    }

    public void NavigateToLedger(EntryKind kind, DateRange range)
    {
        var key = kind == EntryKind.Income ? PageKey.Income : PageKey.Expenses;
        ((LedgerViewModel)Resolve(key)).ApplyExternalRange(range);
        NavigateTo(key);
    }

    public async Task ReloadAllAsync()
    {
        foreach (var page in _pages.Values)
            await page.ReloadAsync();
    }

    private PageViewModel Resolve(PageKey key)
    {
        if (_pages.TryGetValue(key, out var page))
            return page;

        page = key switch
        {
            PageKey.Summary => services.GetRequiredService<SummaryViewModel>(),
            PageKey.Income => services.GetRequiredService<IncomeViewModel>(),
            PageKey.Expenses => services.GetRequiredService<ExpensesViewModel>(),
            PageKey.Accounts => services.GetRequiredService<AccountsViewModel>(),
            PageKey.Settings => services.GetRequiredService<SettingsViewModel>(),
            PageKey.About => services.GetRequiredService<AboutViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
        _pages[key] = page;
        return page;
    }
}
