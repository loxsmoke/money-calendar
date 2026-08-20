using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MoneyCalendar.App.Services;
using MoneyCalendar.Core;
using MoneyCalendar.Core.Abstractions;

namespace MoneyCalendar.App.ViewModels;

public sealed record NavItem(PageKey Key, string Label, string Glyph);

/// <summary>Shell: the four-section sidebar, the page host, and the status bar.</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IEntryRepository _entries;
    private readonly ISettingsStore _settings;
    private readonly IClock _clock;
    private bool _selectionFromNavigation;

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new(PageKey.Summary, "Summary", "📊"),
        new(PageKey.Income, "Income", "💰"),
        new(PageKey.Expenses, "Expenses", "🧾"),
        new(PageKey.Accounts, "Accounts", "🏦"),
        new(PageKey.Settings, "Settings", "⚙️"),
        new(PageKey.About, "About", "ℹ️"),
    ];

    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private string _todayText = "";

    /// <summary>Product name and the numeric version, e.g. "Money Calendar - 0.1.0".</summary>
    public string WindowTitle { get; } = BuildWindowTitle(SystemInfo.NumericVersion());

    internal static string BuildWindowTitle(string version) => $"{Brand.AppName} - {version}";

    public MainWindowViewModel(
        INavigationService navigation,
        IEntryRepository entries,
        ISettingsStore settings,
        IClock clock)
    {
        _navigation = navigation;
        _entries = entries;
        _settings = settings;
        _clock = clock;

        _navigation.CurrentChanged += OnNavigated;

        TodayText = _clock.Today.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        _navigation.NavigateTo(PageKey.Summary);
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (!_selectionFromNavigation && value is not null && value.Key != _navigation.CurrentKey)
            _navigation.NavigateTo(value.Key);
    }

    private void OnNavigated()
    {
        CurrentPage = _navigation.Current;
        _selectionFromNavigation = true;
        try
        {
            SelectedNavItem = NavItems.FirstOrDefault(i => i.Key == _navigation.CurrentKey);
        }
        finally
        {
            _selectionFromNavigation = false;
        }

    }
}
