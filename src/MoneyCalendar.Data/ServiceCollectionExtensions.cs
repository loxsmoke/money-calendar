using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Data.Repositories;

namespace MoneyCalendar.Data;

/// <summary>
/// Builds contexts against the configured database path. The path is read on every call rather
/// than captured, so switching databases is a matter of writing to the options.
/// </summary>
internal sealed class MoneyCalendarDbContextFactory(MoneyCalendarDataOptions options)
    : IDbContextFactory<MoneyCalendarDbContext>
{
    public MoneyCalendarDbContext CreateDbContext() => DatabaseFactory.Open(options.DatabasePath);
}

/// <summary>Contexts for one named file, for the paths the running app is not pointed at.</summary>
public static class DatabaseFactory
{
    public static MoneyCalendarDbContext Open(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var contextOptions = new DbContextOptionsBuilder<MoneyCalendarDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new MoneyCalendarDbContext(contextOptions);
    }

    public static IDbContextFactory<MoneyCalendarDbContext> For(string databasePath) =>
        new MoneyCalendarDbContextFactory(new MoneyCalendarDataOptions { DatabasePath = databasePath });
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the data layer. The host must also register <see cref="IClock"/> and run
    /// <see cref="DatabaseBootstrapper"/> before anything touches a repository.
    /// </summary>
    public static IServiceCollection AddMoneyCalendarData(
        this IServiceCollection services, Action<MoneyCalendarDataOptions>? configure = null)
    {
        var options = new MoneyCalendarDataOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddSingleton<IDbContextFactory<MoneyCalendarDbContext>, MoneyCalendarDbContextFactory>();
        services.AddSingleton<DatabaseBootstrapper>();
        services.AddSingleton<IDatabaseCatalog, DatabaseCatalog>();
        services.AddSingleton<IEntryRepository, EntryRepository>();
        services.AddSingleton<IAccountRepository, AccountRepository>();
        services.AddSingleton<ICategoryRepository, CategoryRepository>();
        return services;
    }
}
