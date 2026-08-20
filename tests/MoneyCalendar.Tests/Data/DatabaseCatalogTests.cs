using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;
using MoneyCalendar.Data;

namespace MoneyCalendar.Tests.Data;

/// <summary>
/// Databases are plain files in one folder. The catalog is the file management around them,
/// and the care it takes is all about the one that is currently open.
/// </summary>
public class DatabaseCatalogTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 8, 18);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "mc-catalog-" + Guid.NewGuid().ToString("N"));

    public DatabaseCatalogTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked handle on a temp folder is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    private TestDatabase Open(string name = "money-calendar") =>
        new(Today, databasePath: Path.Combine(_directory, name + ".db"));

    [Fact]
    public async Task The_open_database_is_listed_and_marked()
    {
        using var db = Open();

        var listed = db.Databases.List();

        Assert.Equal("money-calendar", Assert.Single(listed).Name);
        Assert.Equal("money-calendar", db.Databases.CurrentName);
    }

    [Fact]
    public async Task A_created_database_starts_empty_and_is_not_switched_to()
    {
        using var db = Open();
        await db.Accounts.AddAsync(
            new Account { Name = "Everyday Checking", Type = AccountType.Checking }, CancellationToken.None);

        await db.Databases.CreateAsync("Next year", CancellationToken.None);

        // Still on the original, which still has its account.
        Assert.Equal("money-calendar", db.Databases.CurrentName);
        Assert.Single(await db.Accounts.GetAllAsync(CancellationToken.None));
        // Listed by name, so the folder always reads the same way round.
        Assert.Equal(
            new[] { "money-calendar", "Next year" },
            db.Databases.List().Select(d => d.Name));

        // And the new one is a working, empty ledger.
        await db.Databases.SwitchToAsync("Next year", CancellationToken.None);
        Assert.Empty(await db.Accounts.GetAllAsync(CancellationToken.None));
        Assert.Equal(0, await db.Entries.CountAsync(CancellationToken.None));
        Assert.NotEmpty(await db.Categories.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Switching_changes_what_every_repository_reads()
    {
        using var db = Open();
        await db.AddAsync(new DateOnly(2026, 8, 5), 40m, EntryKind.Expense, DefaultCategories.Groceries);
        await db.Databases.CreateAsync("Scratch", CancellationToken.None);

        await db.Databases.SwitchToAsync("Scratch", CancellationToken.None);
        Assert.Equal(0, await db.Entries.CountAsync(CancellationToken.None));

        await db.Databases.SwitchToAsync("money-calendar", CancellationToken.None);
        Assert.Equal(1, await db.Entries.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_clone_carries_the_data_and_then_goes_its_own_way()
    {
        using var db = Open();
        await db.AddAsync(new DateOnly(2026, 8, 5), 40m, EntryKind.Expense, DefaultCategories.Groceries);

        await db.Databases.CloneAsync("money-calendar", "Backup", CancellationToken.None);
        await db.AddAsync(new DateOnly(2026, 8, 6), 25m, EntryKind.Expense, DefaultCategories.Transport);

        Assert.Equal(2, await db.Entries.CountAsync(CancellationToken.None));

        // The copy holds what was there when it was taken, and nothing since.
        await db.Databases.SwitchToAsync("Backup", CancellationToken.None);
        Assert.Equal(1, await db.Entries.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Renaming_the_open_database_keeps_it_open()
    {
        using var db = Open();
        await db.AddAsync(new DateOnly(2026, 8, 5), 40m, EntryKind.Expense, DefaultCategories.Groceries);

        db.Databases.Rename("money-calendar", "Household");

        Assert.Equal("Household", db.Databases.CurrentName);
        Assert.Equal("Household", Assert.Single(db.Databases.List()).Name);
        Assert.Equal(1, await db.Entries.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_open_database_cannot_be_deleted()
    {
        using var db = Open();

        var error = Assert.Throws<InvalidOperationException>(() => db.Databases.Delete("money-calendar"));

        Assert.Contains("in use", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(db.Databases.List());
    }

    [Fact]
    public async Task Deleting_another_database_takes_its_file_with_it()
    {
        using var db = Open();
        var path = await db.Databases.CreateAsync("Scratch", CancellationToken.None);
        Assert.True(File.Exists(path));

        db.Databases.Delete("Scratch");

        Assert.False(File.Exists(path));
        Assert.Equal("money-calendar", Assert.Single(db.Databases.List()).Name);
    }

    [Fact]
    public async Task A_name_already_taken_is_refused()
    {
        using var db = Open();
        await db.Databases.CreateAsync("Scratch", CancellationToken.None);

        var created = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Databases.CreateAsync("Scratch", CancellationToken.None));
        var renamed = Assert.Throws<InvalidOperationException>(
            () => db.Databases.Rename("money-calendar", "Scratch"));

        Assert.Contains("already exists", created.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("already exists", renamed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("books/2026")]
    [InlineData(@"books\2026")]
    [InlineData("what?")]
    [InlineData(".hidden")]
    public void A_name_that_is_not_a_file_name_is_refused(string name)
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseCatalog.Validate(name));
    }

    [Fact]
    public void A_missing_database_is_refused_rather_than_created_by_accident()
    {
        using var db = Open();

        var error = Assert.Throws<InvalidOperationException>(() => db.Databases.Delete("Never existed"));

        Assert.Contains("no longer there", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
