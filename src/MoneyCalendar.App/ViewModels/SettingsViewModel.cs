using System.Globalization;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneyCalendar.App.Services;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;
using MoneyCalendar.Core.Services;
using MoneyCalendar.Data;
using Serilog;

namespace MoneyCalendar.App.ViewModels;

/// <summary>One category in the Settings list.</summary>
public sealed class CategoryRowViewModel(Category category) : ViewModelBase
{
    public Category Category { get; } = category;

    public Guid Id => Category.Id;
    public string Name => Category.Name;
    public bool IsSystem => Category.IsSystem;
    public IBrush Color { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(category.ColorHex));
    public string BadgeText { get; } = category.IsSystem ? "built-in" : "custom";
}

/// <summary>One database file in the Developer section's list.</summary>
public sealed class DatabaseRowViewModel(DatabaseFile file, bool isCurrent) : ViewModelBase
{
    public DatabaseFile File { get; } = file;

    public string Name => File.Name;
    public string Path => File.Path;
    public bool IsCurrent { get; } = isCurrent;

    /// <summary>Only another database can be switched to, renamed onto, or deleted.</summary>
    public bool IsNotCurrent => !IsCurrent;

    public string BadgeText => IsCurrent ? "in use" : "";

    public string DetailText =>
        $"{Format.FileSize(File.SizeBytes)} · {File.ModifiedAt.ToLocalTime().ToString("MMM d, yyyy HH:mm", CultureInfo.CurrentCulture)}";
}

/// <summary>
/// The categories of one kind, as the Settings lists group them. The group is collapsible, and
/// it reports every change so the page can put it back the same way after a reload.
/// </summary>
public sealed partial class CategoryGroup : ViewModelBase
{
    private readonly Action<string, bool>? _expansionChanged;

    public CategoryGroup(
        string key,
        string title,
        IReadOnlyList<CategoryRowViewModel> categories,
        bool isExpanded,
        Action<string, bool>? expansionChanged = null)
    {
        Key = key;
        Title = title;
        Categories = categories;
        _isExpanded = isExpanded;
        _expansionChanged = expansionChanged;
    }

    /// <summary>Identifies the group across reloads, which is what the expanded state is kept by.</summary>
    public string Key { get; }

    public string Title { get; }
    public IReadOnlyList<CategoryRowViewModel> Categories { get; }

    /// <summary>"8 categories" — the header carries the count a collapsed group is hiding.</summary>
    public string CountText => Format.Count(Categories.Count, "category", "categories");

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => _expansionChanged?.Invoke(Key, value);
}

/// <summary>
/// Settings: the currency and budget the other sections read, appearance, and the data
/// export/import surface (JSON backup for round-tripping, CSV for spreadsheets).
/// </summary>
public partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IDataTransferService _transfer;
    private readonly IEntryRepository _entries;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly IDatabaseCatalog _databases;
    private readonly MoneyCalendar.Data.MoneyCalendarDataOptions _dataOptions;

    /// <summary>Which groups the user left open, so a reload does not fold them all back up.</summary>
    private readonly Dictionary<string, bool> _expandedGroups = [];
    private bool _suppressSave;

    public override string Title => "Settings";

    public IReadOnlyList<string> Themes { get; } = ["Default", "Light", "Dark"];
    public IReadOnlyList<string> ImportModes { get; } = ["Merge with existing data", "Replace all data"];

    [ObservableProperty] private string _selectedTheme = "Default";
    [ObservableProperty] private string _selectedImportMode = "Merge with existing data";

    [ObservableProperty] private string _transactionCountText = "";

    /// <summary>Stored transactions, as a number: the delete dialog needs it, not its text.</summary>
    public int TransactionCount { get; private set; }

    /// <summary>Stored accounts, as a number, for the same reason.</summary>
    public int AccountCount { get; private set; }
    [ObservableProperty] private string _accountCountText = "";
    [ObservableProperty] private string _repeatingCountText = "";
    [ObservableProperty] private string _customCategoryCountText = "";
    [ObservableProperty] private string _databasePathText = "";

    /// <summary>Every database in the data folder, the open one marked.</summary>
    [ObservableProperty] private IReadOnlyList<DatabaseRowViewModel> _databaseRows = [];
    [ObservableProperty] private string _currentDatabaseName = "";
    [ObservableProperty] private string? _databaseStatusText;

    /// <summary>The categories that ship with the app, grouped by kind.</summary>
    [ObservableProperty] private IReadOnlyList<CategoryGroup> _categoryGroups = [];

    /// <summary>The ones the user added, grouped the same way. Empty on a fresh install.</summary>
    [ObservableProperty] private IReadOnlyList<CategoryGroup> _customCategoryGroups = [];
    [ObservableProperty] private bool _hasCustomCategories;
    [ObservableProperty] private string? _categoryStatusText;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private bool _isBusy;

    public ImportMode SelectedMode =>
        SelectedImportMode.StartsWith("Replace", StringComparison.Ordinal) ? ImportMode.Replace : ImportMode.Merge;

    public SettingsViewModel(
        ISettingsStore settings,
        IDataTransferService transfer,
        IEntryRepository entries,
        IAccountRepository accounts,
        ICategoryRepository categories,
        INavigationService navigation,
        IClock clock,
        IDatabaseCatalog databases,
        MoneyCalendar.Data.MoneyCalendarDataOptions dataOptions)
    {
        _settings = settings;
        _transfer = transfer;
        _entries = entries;
        _accounts = accounts;
        _categories = categories;
        _navigation = navigation;
        _clock = clock;
        _databases = databases;
        _dataOptions = dataOptions;
    }

    protected override async Task<bool> LoadAsync(CancellationToken ct)
    {
        _suppressSave = true;
        try
        {
            SelectedTheme = _settings.Current.Theme;
        }
        finally
        {
            _suppressSave = false;
        }

        DatabasePathText = _dataOptions.DatabasePath;
        LoadDatabases();

        // What the database actually holds, counted from the stored rows: a repeating series
        // is one transaction here however many occurrences it draws on the calendar.
        var stored = await _entries.GetAsync(new EntryFilter(), ct);
        TransactionCount = stored.Count;
        TransactionCountText = stored.Count.ToString("N0", CultureInfo.CurrentCulture);
        RepeatingCountText = stored.Count(e => e.IsRecurring).ToString("N0", CultureInfo.CurrentCulture);
        AccountCount = (await _accounts.GetAllAsync(ct)).Count;
        AccountCountText = AccountCount.ToString("N0", CultureInfo.CurrentCulture);
        OnPropertyChanged(nameof(CanLoadSampleData));

        // Only the categories the user made: the built-in ones ship with every install, so
        // counting them here says nothing about what this ledger holds.
        var categories = await _categories.GetAllAsync(ct);
        CustomCategoryCountText = categories.Count(c => !c.IsSystem).ToString("N0", CultureInfo.CurrentCulture);

        // Built-ins are a long, stable list, so they start folded away; the handful the user
        // added is the part worth having open.
        CategoryGroups = BuildGroups(categories.Where(c => c.IsSystem), "builtin", expandedByDefault: false);
        CustomCategoryGroups = BuildGroups(categories.Where(c => !c.IsSystem), "custom", expandedByDefault: true);
        HasCustomCategories = CustomCategoryGroups.Count > 0;
        return true;
    }

    /// <summary>
    /// One group per kind, in income-then-expense order. A kind with nothing in it is left out
    /// rather than drawn as an empty expander.
    /// </summary>
    private IReadOnlyList<CategoryGroup> BuildGroups(
        IEnumerable<Category> source, string keyPrefix, bool expandedByDefault) =>
        source
            .GroupBy(c => c.Kind)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var key = $"{keyPrefix}:{g.Key}";
                return new CategoryGroup(
                    key,
                    g.Key == EntryKind.Income ? "Income types" : "Expense categories",
                    g.Select(c => new CategoryRowViewModel(c)).ToList(),
                    _expandedGroups.TryGetValue(key, out var open) ? open : expandedByDefault,
                    RememberExpansion);
            })
            .ToList();

    private void RememberExpansion(string key, bool isExpanded) => _expandedGroups[key] = isExpanded;

    // ---- preferences ------------------------------------------------------

    partial void OnSelectedThemeChanged(string value)
    {
        ApplyTheme(value);
        if (_suppressSave)
            return;

        ErrorText = null;
        _ = _settings.SaveAsync(_settings.Current with { Theme = SelectedTheme }, CancellationToken.None);
    }

    private static void ApplyTheme(string theme)
    {
        if (Avalonia.Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    // ---- categories -------------------------------------------------------

    /// <summary>Editor state for the dialog; the view owns the window, this owns the data.</summary>
    public async Task<CategoryEditorViewModel> CreateCategoryEditorAsync(CategoryRowViewModel? row)
    {
        var all = await _categories.GetAllAsync(CancellationToken.None);
        return new CategoryEditorViewModel(row?.Category, all, EntryKind.Expense);
    }

    /// <summary>Commits a dialog result. Returns false when validation failed.</summary>
    public async Task<bool> SaveCategoryAsync(CategoryEditorViewModel editor)
    {
        if (editor.TryBuild() is not { } category)
            return false;

        if (editor.IsNew)
        {
            await _categories.AddAsync(category, CancellationToken.None);
            CategoryStatusText = $"Added {category.Name}.";
        }
        else
        {
            await _categories.UpdateAsync(category, CancellationToken.None);
            CategoryStatusText = $"Updated {category.Name}.";
        }

        await ReloadAsync();
        await _navigation.ReloadAllAsync();
        return true;
    }

    /// <summary>
    /// Deletes a category. Built-in ones and any still holding entries are refused by the
    /// repository, and the reason is reported rather than swallowed.
    /// </summary>
    public async Task DeleteCategoryAsync(CategoryRowViewModel row)
    {
        var deleted = await _categories.TryDeleteAsync(row.Id, CancellationToken.None);
        CategoryStatusText = deleted
            ? $"Deleted {row.Name}."
            : row.IsSystem
                ? $"{row.Name} is a built-in category and cannot be deleted."
                : $"{row.Name} still has entries filed under it, so it was kept.";

        await ReloadAsync();
        await _navigation.ReloadAllAsync();
    }

    // ---- data export / import --------------------------------------------

    public string SuggestedJsonName =>
        $"money-calendar-backup-{_clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";

    public string SuggestedCsvName =>
        $"money-calendar-{_clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";

    public async Task ExportJsonAsync(string path) =>
        await RunAsync(async () =>
        {
            var count = await _transfer.ExportJsonAsync(path, CancellationToken.None);
            return $"Exported {Format.Count(count, "entry", "entries")} to {Path.GetFileName(path)}.";
        });

    public async Task ExportCsvAsync(string path) =>
        await RunAsync(async () =>
        {
            var count = await _transfer.ExportCsvAsync(path, CancellationToken.None);
            return $"Exported {Format.Count(count, "entry", "entries")} to {Path.GetFileName(path)}.";
        });

    public async Task ImportJsonAsync(string path) =>
        await RunAsync(async () =>
        {
            var result = await _transfer.ImportJsonAsync(path, SelectedMode, CancellationToken.None);
            return DescribeImport(result);
        });

    public async Task ImportCsvAsync(string path) =>
        await RunAsync(async () =>
        {
            var result = await _transfer.ImportCsvAsync(path, SelectedMode, CancellationToken.None);
            return DescribeImport(result);
        });

    /// <summary>
    /// True when there is nothing to lose: no transactions and no accounts. The demo ledger
    /// brings its own accounts and files everything through them, so dropping it on top of real
    /// books would mix invented entries into them with no way to tell which were which.
    /// </summary>
    public bool CanLoadSampleData => TransactionCount == 0 && AccountCount == 0;

    [RelayCommand]
    private async Task LoadSampleDataAsync() =>
        await RunAsync(async () =>
        {
            if (!CanLoadSampleData)
            {
                throw new InvalidOperationException(
                    "The database is not empty, so the sample data cannot be added. It brings its " +
                    "own accounts and entries, and there would be no telling them apart from yours " +
                    "afterwards. Delete all data first if you want the demo ledger.");
            }

            // The demo ledger names the accounts and categories it files against, so put those
            // in place first: on an empty ledger there is nothing to file against.
            await _accounts.AddMissingAsync(
                SampleData.BuildAccounts(_clock.UtcNow), CancellationToken.None);
            await _categories.AddMissingAsync(SampleData.BuildCategories(), CancellationToken.None);

            var accounts = await _accounts.GetAllAsync(CancellationToken.None);
            var sample = SampleData.Build(
                _clock.Today, _settings.Current.CurrencyCode, _clock.UtcNow, accounts);
            var added = await _entries.AddRangeAsync(sample, CancellationToken.None);
            return $"Added {Format.Count(added, "sample entry", "sample entries")}.";
        });

    /// <summary>Nothing to confirm when there is nothing to delete.</summary>
    public void ReportNothingToDelete()
    {
        ErrorText = null;
        StatusText = "There is nothing to delete.";
    }

    [RelayCommand]
    private async Task ClearTransactionsAsync() =>
        await RunAsync(async () =>
        {
            var deleted = await _entries.DeleteAllAsync(CancellationToken.None);
            return $"Deleted {Format.Count(deleted, "transaction", "transactions")}. " +
                "Accounts and categories were kept.";
        });

    /// <summary>The wider wipe: transactions first, then the accounts they hung off.</summary>
    [RelayCommand]
    private async Task ClearAllDataAsync() =>
        await RunAsync(async () =>
        {
            var entries = await _entries.DeleteAllAsync(CancellationToken.None);
            var accounts = await _accounts.DeleteAllAsync(CancellationToken.None);
            return $"Deleted {Format.Count(entries, "transaction", "transactions")} and " +
                $"{Format.Count(accounts, "account", "accounts")}. Categories were kept.";
        });

    // ---- databases (Developer) --------------------------------------------

    /// <summary>Where new databases are created and the existing ones are listed from.</summary>
    public string DatabaseDirectory => _databases.Directory;

    private void LoadDatabases()
    {
        CurrentDatabaseName = _databases.CurrentName;
        DatabaseRows = _databases.List()
            .Select(d => new DatabaseRowViewModel(
                d, string.Equals(d.Name, CurrentDatabaseName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Creates an empty database and leaves the app on the one it is already using.</summary>
    public async Task CreateDatabaseAsync(string name) =>
        await RunDatabaseAsync(async () =>
        {
            await _databases.CreateAsync(name, CancellationToken.None);
            return $"Created {name}. Select it to start using it.";
        });

    /// <summary>Copies a database under a new name, the open one included.</summary>
    public async Task CloneDatabaseAsync(DatabaseRowViewModel row, string name) =>
        await RunDatabaseAsync(async () =>
        {
            await _databases.CloneAsync(row.Name, name, CancellationToken.None);
            return $"Copied {row.Name} to {name}.";
        });

    /// <summary>
    /// Points the whole app at another database: the pages are all reloaded, and the choice is
    /// remembered so the next launch opens the same one.
    /// </summary>
    public async Task SelectDatabaseAsync(DatabaseRowViewModel row) =>
        await RunDatabaseAsync(async () =>
        {
            await _databases.SwitchToAsync(row.Name, CancellationToken.None);
            await _settings.SaveAsync(
                _settings.Current with { DatabaseName = row.Name }, CancellationToken.None);
            return $"Now using {row.Name}.";
        });

    public async Task RenameDatabaseAsync(DatabaseRowViewModel row, string name) =>
        await RunDatabaseAsync(async () =>
        {
            _databases.Rename(row.Name, name);
            if (row.IsCurrent)
            {
                await _settings.SaveAsync(
                    _settings.Current with { DatabaseName = name }, CancellationToken.None);
            }

            return $"Renamed {row.Name} to {name}.";
        });

    public async Task DeleteDatabaseAsync(DatabaseRowViewModel row) =>
        await RunDatabaseAsync(() =>
        {
            _databases.Delete(row.Name);
            return Task.FromResult($"Deleted {row.Name}.");
        });

    /// <summary>
    /// Database work reports into its own status line, next to the list it acts on, and always
    /// reloads every page — switching databases changes what the whole app is looking at.
    /// </summary>
    private async Task RunDatabaseAsync(Func<Task<string>> operation)
    {
        IsBusy = true;
        DatabaseStatusText = null;
        ErrorText = null;
        try
        {
            DatabaseStatusText = await operation();
            await ReloadAsync();
            await _navigation.ReloadAllAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Database operation failed");
            ErrorText = ex.Message;
            LoadDatabases();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database operation failed");
            ErrorText = "That did not work. See the log for details.";
            LoadDatabases();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAsync(Func<Task<string>> operation)
    {
        IsBusy = true;
        StatusText = null;
        ErrorText = null;
        try
        {
            StatusText = await operation();
            await ReloadAsync();
            await _navigation.ReloadAllAsync();
        }
        catch (Exception ex) when (ex is ImportFormatException or InvalidOperationException)
        {
            ErrorText = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Data transfer failed");
            ErrorText = $"The file could not be read or written: {ex.Message}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Data transfer failed");
            ErrorText = "That did not work. See the log for details.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string DescribeImport(ImportResult result)
    {
        var parts = new List<string>
        {
            $"Imported {Format.Count(result.EntriesImported, "entry", "entries")}",
        };
        if (result.EntriesSkipped > 0)
            parts.Add($"{result.EntriesSkipped} already present");
        if (result.CategoriesImported > 0)
            parts.Add(Format.Count(result.CategoriesImported, "new category", "new categories"));
        return string.Join(" · ", parts) + ".";
    }
}
