using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Data;
using MoneyCalendar.Data.Repositories;

namespace MoneyCalendar.Tests;

/// <summary>Clock frozen at a known date so range and calendar assertions are stable.</summary>
public sealed class FakeClock(DateOnly today) : IClock
{
    public DateOnly Today { get; set; } = today;
    public DateTimeOffset UtcNow => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
}

/// <summary>
/// A real SQLite database in a temp directory. The repositories run against the same provider
/// the app uses, so date/decimal storage quirks show up in tests rather than at runtime.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _directory;
    private readonly bool _ownsDirectory;

    public TestDatabase(DateOnly? today = null, bool seedSample = false, string? databasePath = null)
    {
        _directory = databasePath is null
            ? Path.Combine(Path.GetTempPath(), "money-calendar-tests", Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        _ownsDirectory = databasePath is null;
        Directory.CreateDirectory(_directory);

        Clock = new FakeClock(today ?? new DateOnly(2026, 8, 18));
        Options = new MoneyCalendarDataOptions
        {
            DatabasePath = databasePath ?? Path.Combine(_directory, "test.db"),
            SeedSampleDataOnFirstRun = seedSample,
        };

        // Reads the path from the options on every call, exactly as the app's factory does, so
        // switching databases moves the repositories with it.
        ContextFactory = new TestContextFactory(Options);
        Entries = new EntryRepository(ContextFactory, Clock);
        Accounts = new AccountRepository(ContextFactory, Clock);
        Categories = new CategoryRepository(ContextFactory);
        Queries = new MoneyCalendar.Core.Services.EntryQueryService(Entries);
        Summaries = new MoneyCalendar.Core.Services.SummaryService(Queries);

        Bootstrapper = new DatabaseBootstrapper(
            Options, ContextFactory, Clock, NullLogger<DatabaseBootstrapper>.Instance);
        Databases = new DatabaseCatalog(Options, Bootstrapper);
        Bootstrapper.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public FakeClock Clock { get; }
    public MoneyCalendarDataOptions Options { get; }
    public IDbContextFactory<MoneyCalendarDbContext> ContextFactory { get; }
    public IEntryRepository Entries { get; }
    public IAccountRepository Accounts { get; }
    public ICategoryRepository Categories { get; }
    public IEntryQueryService Queries { get; }
    public ISummaryService Summaries { get; }
    public DatabaseBootstrapper Bootstrapper { get; }
    public IDatabaseCatalog Databases { get; }

    public string TempFile(string name) => Path.Combine(_directory, name);

    public Task<Entry> AddAsync(
        DateOnly date, decimal amount, EntryKind kind, Guid categoryId, string? note = null) =>
        Entries.AddAsync(
            new Entry
            {
                Date = date,
                Amount = amount,
                Kind = kind,
                CategoryId = categoryId,
                CurrencyCode = "USD",
                Note = note,
            },
            CancellationToken.None);

    public void Dispose()
    {
        // SQLite keeps the file handle until pooled connections are dropped.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!_ownsDirectory)
            return;

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestContextFactory(MoneyCalendarDataOptions options)
        : IDbContextFactory<MoneyCalendarDbContext>
    {
        public MoneyCalendarDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<MoneyCalendarDbContext>()
                .UseSqlite($"Data Source={options.DatabasePath}")
                .Options);
    }
}
