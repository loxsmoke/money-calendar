using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.Data;

/// <summary>
/// Creates or opens the SQLite database and seeds it. The prototype uses EnsureCreated rather
/// than EF migrations: there is no shipped schema to upgrade yet, and the JSON backup in
/// Settings is the supported way to carry data across a schema change.
/// </summary>
public sealed class DatabaseBootstrapper(
    MoneyCalendarDataOptions options,
    IDbContextFactory<MoneyCalendarDbContext> contextFactory,
    IClock clock,
    ILogger<DatabaseBootstrapper> logger)
{
    /// <summary>Opens (and if needed creates) the configured database.</summary>
    public Task InitializeAsync(CancellationToken ct) =>
        InitializeAsync(options.DatabasePath, options.SeedSampleDataOnFirstRun, ct);

    /// <summary>
    /// The same, against an explicit file. The Developer section uses this to bring a database
    /// into being without pointing the running app at it first.
    /// </summary>
    public async Task InitializeAsync(string databasePath, bool seedSampleData, CancellationToken ct)
    {
        SQLitePCL.Batteries_V2.Init();
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // The injected factory follows the running app's database; a different file needs one
        // of its own rather than a temporary edit to the shared options.
        var factory = string.Equals(databasePath, options.DatabasePath, StringComparison.OrdinalIgnoreCase)
            ? contextFactory
            : DatabaseFactory.For(databasePath);

        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var created = await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        await EnsureLaterTablesAsync(db, ct).ConfigureAwait(false);
        await SeedCategoriesAsync(db, ct).ConfigureAwait(false);

        if (created && seedSampleData)
            await SeedSampleEntriesAsync(db, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Database ready at {Path} (created: {Created})", databasePath, created);
    }

    /// <summary>
    /// EnsureCreated only builds the schema when the file is new, so a database created by an
    /// earlier build never gets tables or columns added later. Until this prototype grows real
    /// migrations, anything introduced after the first release is applied here by hand.
    /// </summary>
    private static async Task EnsureLaterTablesAsync(MoneyCalendarDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Accounts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Accounts" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Type" INTEGER NOT NULL,
                "Last4" TEXT NULL,
                "Note" TEXT NULL,
                "CreatedAt" INTEGER NOT NULL,
                "UpdatedAt" INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Accounts_Name" ON "Accounts" ("Name");
            """,
            ct).ConfigureAwait(false);

        // The account link and the recurrence pattern arrived after the Entries table shipped.
        (string Column, string Definition)[] entryColumns =
        [
            ("AccountId", "TEXT NULL"),
            ("ToAccountId", "TEXT NULL"),
            ("Frequency", "INTEGER NOT NULL DEFAULT 0"),
            ("DayOfMonth", "INTEGER NULL"),
            ("SecondDayOfMonth", "INTEGER NULL"),
            ("SecondDayMode", "INTEGER NOT NULL DEFAULT 0"),
            ("Weekday", "INTEGER NULL"),
            ("RecurrenceEnd", "TEXT NULL"),
        ];

        var existing = await ColumnNamesAsync(db, "Entries", ct).ConfigureAwait(false);
        foreach (var (column, definition) in entryColumns.Where(c => !existing.Contains(c.Column)))
        {
            // Column names and types cannot be parameterized, and both come from the constant
            // list above rather than from anything a user can influence.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"""ALTER TABLE "Entries" ADD COLUMN "{column}" {definition};""", ct).ConfigureAwait(false);
#pragma warning restore EF1002
        }
    }

    private static async Task<HashSet<string>> ColumnNamesAsync(
        MoneyCalendarDbContext db, string table, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            names.Add(reader.GetString(1));
        return names;
    }

    /// <summary>Inserts any missing built-in categories; ids are deterministic so this is idempotent.</summary>
    private static async Task SeedCategoriesAsync(MoneyCalendarDbContext db, CancellationToken ct)
    {
        var existingIds = await db.Categories.Select(c => c.Id).ToHashSetAsync(ct).ConfigureAwait(false);
        var missing = DefaultCategories.All.Where(c => !existingIds.Contains(c.Id)).ToList();
        if (missing.Count == 0)
            return;

        // Seed instances are shared statics — copy so EF never tracks (and mutates) them.
        db.Categories.AddRange(missing.Select(c => new Category
        {
            Id = c.Id,
            Name = c.Name,
            Kind = c.Kind,
            ColorHex = c.ColorHex,
            IsSystem = c.IsSystem,
            WantsAccountDetails = c.WantsAccountDetails,
            SortOrder = c.SortOrder,
        }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task SeedSampleEntriesAsync(MoneyCalendarDbContext db, CancellationToken ct)
    {
        if (await db.Entries.AnyAsync(ct).ConfigureAwait(false))
            return;

        // Accounts first: the sample entries point at them.
        var accounts = await db.Accounts.AnyAsync(ct).ConfigureAwait(false)
            ? await db.Accounts.ToListAsync(ct).ConfigureAwait(false)
            : SampleData.BuildAccounts(clock.UtcNow).ToList();
        if (!await db.Accounts.AnyAsync(ct).ConfigureAwait(false))
            db.Accounts.AddRange(accounts);

        // The sample bills are filed under categories of its own, which have to exist before
        // an entry can point at one.
        var categoryIds = await db.Categories.Select(c => c.Id).ToHashSetAsync(ct).ConfigureAwait(false);
        db.Categories.AddRange(SampleData.BuildCategories().Where(c => !categoryIds.Contains(c.Id)));

        var sample = SampleData.Build(clock.Today, "USD", clock.UtcNow, accounts);
        db.Entries.AddRange(sample);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Seeded {Count} sample entries", sample.Count);
    }
}
