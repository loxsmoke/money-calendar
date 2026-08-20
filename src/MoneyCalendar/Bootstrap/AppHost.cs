using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MoneyCalendar.Services;
using MoneyCalendar.ViewModels;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Services;
using MoneyCalendar.Data;
using Serilog;

namespace MoneyCalendar.Bootstrap;

/// <summary>
/// Composition root. <see cref="Build"/> is the desktop graph; <see cref="ConfigureServices"/>
/// is the same graph with the caller's settings store and database path, which is what the
/// headless UI tests use.
/// </summary>
public static class AppHost
{
    public static IHost Build()
    {
        StartupTrace.Write("AppHost.Build entered");
        var builder = Host.CreateApplicationBuilder();
        var settingsStore = new JsonSettingsStore(AppDataPaths.SettingsFile);

        builder.Services.AddSerilog(logging => logging
            .WriteTo.File(
                Path.Combine(AppDataPaths.Logs, "money-calendar-.log"),
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14));

        // A new install opens on an empty ledger, not on someone else's accounts. The demo
        // data is one click away under Settings -> Developer for anyone who wants a look.
        ConfigureServices(
            builder.Services, settingsStore, AppDataPaths.DatabaseFor(settingsStore.Current.DatabaseName),
            seedSampleData: false);

        var host = builder.Build();
        StartupTrace.Write("AppHost.Build completed");
        return host;
    }

    public static void ConfigureServices(
        IServiceCollection services, ISettingsStore settingsStore, string databasePath, bool seedSampleData = false)
    {
        // The desktop host layers Serilog on top of this; registering it here keeps the graph
        // self-contained for callers that build it without a generic host (the tests).
        services.AddLogging();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(settingsStore);
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddMoneyCalendarData(options =>
        {
            options.DatabasePath = databasePath;
            options.SeedSampleDataOnFirstRun = seedSampleData;
        });

        services.AddSingleton<IEntryQueryService, EntryQueryService>();
        services.AddSingleton<ISummaryService, SummaryService>();
        services.AddSingleton<IDataTransferService, DataTransferService>();

        // Pages are singletons so range selections and the calendar month survive navigation.
        services.AddSingleton<SummaryViewModel>();
        services.AddSingleton<IncomeViewModel>();
        services.AddSingleton<ExpensesViewModel>();
        services.AddSingleton<AccountsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainWindowViewModel>();
    }
}

/// <summary>Platform data locations: %APPDATA%/MoneyCalendar on Windows, XDG/Library elsewhere.</summary>
public static class AppDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoneyCalendar");

    public static string Logs { get; } = Path.Combine(Root, "logs");
    public static string DatabaseFile { get; } = Path.Combine(Root, "money-calendar.db");
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    /// <summary>
    /// The database of that name in the data folder. A blank or missing name — or one whose file
    /// has been deleted from outside the app — falls back to the default, so a stale setting
    /// cannot stop the app from starting.
    /// </summary>
    public static string DatabaseFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DatabaseFile;

        var path = Path.Combine(Root, name.Trim() + ".db");
        return File.Exists(path) ? path : DatabaseFile;
    }
}
