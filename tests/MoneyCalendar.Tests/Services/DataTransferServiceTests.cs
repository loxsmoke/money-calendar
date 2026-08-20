using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;
using MoneyCalendar.Core.Services;

namespace MoneyCalendar.Tests.Services;

public class DataTransferServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 18);

    private static DataTransferService Transfer(TestDatabase db) =>
        new(db.Entries, db.Categories, db.Accounts, db.Clock);

    [Fact]
    public async Task Json_backup_round_trips_entries_into_an_empty_database()
    {
        using var source = new TestDatabase(Today);
        await source.AddAsync(new DateOnly(2026, 8, 1), 1650m, EntryKind.Expense, DefaultCategories.Rent, "Apartment");
        await source.AddAsync(new DateOnly(2026, 8, 15), 2450m, EntryKind.Income, DefaultCategories.Salary, "Payroll");
        var path = source.TempFile("backup.json");

        var exported = await Transfer(source).ExportJsonAsync(path, CancellationToken.None);
        Assert.Equal(2, exported);

        using var target = new TestDatabase(Today);
        var result = await Transfer(target).ImportJsonAsync(path, ImportMode.Merge, CancellationToken.None);

        Assert.Equal(2, result.EntriesImported);
        var rows = await target.Entries.GetAsync(new EntryFilter(), CancellationToken.None);
        Assert.Equal(2, rows.Count);
        var rent = rows.Single(e => e.Kind == EntryKind.Expense);
        Assert.Equal(1650m, rent.Amount);
        Assert.Equal(new DateOnly(2026, 8, 1), rent.Date);
        Assert.Equal("Apartment", rent.Note);
    }

    [Fact]
    public async Task Merge_import_skips_entries_that_are_already_present()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 1), 100m, EntryKind.Expense, DefaultCategories.Fee);
        var path = db.TempFile("backup.json");
        await Transfer(db).ExportJsonAsync(path, CancellationToken.None);

        var result = await Transfer(db).ImportJsonAsync(path, ImportMode.Merge, CancellationToken.None);

        Assert.Equal(0, result.EntriesImported);
        Assert.Equal(1, result.EntriesSkipped);
        Assert.Equal(1, await db.Entries.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Replace_import_drops_existing_entries_first()
    {
        using var source = new TestDatabase(Today);
        await source.AddAsync(new DateOnly(2026, 8, 5), 25m, EntryKind.Expense, DefaultCategories.Fee);
        var path = source.TempFile("backup.json");
        await Transfer(source).ExportJsonAsync(path, CancellationToken.None);

        using var target = new TestDatabase(Today);
        await target.AddAsync(new DateOnly(2026, 7, 1), 900m, EntryKind.Expense, DefaultCategories.Rent);
        await target.AddAsync(new DateOnly(2026, 7, 2), 900m, EntryKind.Income, DefaultCategories.Salary);

        var result = await Transfer(target).ImportJsonAsync(path, ImportMode.Replace, CancellationToken.None);

        Assert.Equal(1, result.EntriesImported);
        var rows = await target.Entries.GetAsync(new EntryFilter(), CancellationToken.None);
        Assert.Single(rows);
        Assert.Equal(25m, rows[0].Amount);
    }

    [Fact]
    public async Task Json_backup_carries_accounts_across()
    {
        using var source = new TestDatabase(Today);
        await source.Accounts.AddAsync(
            new Account { Name = "Sapphire Visa", Type = AccountType.Credit, Last4 = "4417" },
            CancellationToken.None);
        await source.Accounts.AddAsync(
            new Account { Name = "Brokerage", Type = AccountType.Investment },
            CancellationToken.None);
        var path = source.TempFile("backup.json");
        await Transfer(source).ExportJsonAsync(path, CancellationToken.None);

        using var target = new TestDatabase(Today);
        await Transfer(target).ImportJsonAsync(path, ImportMode.Merge, CancellationToken.None);

        var accounts = await target.Accounts.GetAllAsync(CancellationToken.None);
        Assert.Equal(2, accounts.Count);
        var card = accounts.Single(a => a.Name == "Sapphire Visa");
        Assert.Equal(AccountType.Credit, card.Type);
        Assert.Equal("4417", card.Last4);
    }

    [Fact]
    public async Task Json_backup_without_an_accounts_section_still_imports()
    {
        using var db = new TestDatabase(Today);
        var path = db.TempFile("legacy.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "version": 1,
              "exportedAt": "2026-08-18T12:00:00+00:00",
              "categories": [],
              "entries": [
                {
                  "id": "11111111-1111-4111-8111-111111111111",
                  "date": "2026-08-05",
                  "amount": 42.5,
                  "kind": "Expense",
                  "categoryId": "0000000a-0000-4000-8000-000000000026",
                  "currencyCode": "USD"
                }
              ]
            }
            """);

        var result = await Transfer(db).ImportJsonAsync(path, ImportMode.Merge, CancellationToken.None);

        Assert.Equal(1, result.EntriesImported);
        Assert.Empty(await db.Accounts.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Json_backup_carries_account_links_and_repeat_patterns()
    {
        using var source = new TestDatabase(Today);
        var account = await source.Accounts.AddAsync(
            new Account { Name = "Everyday", Type = AccountType.Checking }, CancellationToken.None);
        var card = await source.Accounts.AddAsync(
            new Account { Name = "Visa", Type = AccountType.Credit }, CancellationToken.None);
        await source.Entries.AddAsync(
            new Entry
            {
                Date = new DateOnly(2026, 8, 1),
                Amount = 1650m,
                Kind = EntryKind.Expense,
                CategoryId = DefaultCategories.Rent,
                CurrencyCode = "USD",
                AccountId = account.Id,
                ToAccountId = card.Id,
                Frequency = RecurrenceFrequency.TwiceMonthly,
                DayOfMonth = 1,
                SecondDayMode = MonthDayMode.LastDay,
                RecurrenceEnd = new DateOnly(2027, 1, 31),
            },
            CancellationToken.None);
        var path = source.TempFile("backup.json");
        await Transfer(source).ExportJsonAsync(path, CancellationToken.None);

        using var target = new TestDatabase(Today);
        await Transfer(target).ImportJsonAsync(path, ImportMode.Merge, CancellationToken.None);

        var restored = (await target.Entries.GetAsync(new EntryFilter(), CancellationToken.None)).Single();
        Assert.Equal(account.Id, restored.AccountId);
        Assert.Equal(card.Id, restored.ToAccountId);
        Assert.Equal(RecurrenceFrequency.TwiceMonthly, restored.Frequency);
        Assert.Equal(1, restored.DayOfMonth);
        Assert.Equal(MonthDayMode.LastDay, restored.SecondDayMode);
        Assert.Equal(new DateOnly(2027, 1, 31), restored.RecurrenceEnd);
    }

    [Fact]
    public async Task An_import_without_the_accounts_drops_the_links_rather_than_dangling()
    {
        using var db = new TestDatabase(Today);
        var path = db.TempFile("no-accounts.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "version": 1,
              "exportedAt": "2026-08-18T12:00:00+00:00",
              "categories": [],
              "entries": [
                {
                  "id": "22222222-2222-4222-8222-222222222222",
                  "date": "2026-08-05",
                  "amount": 42.5,
                  "kind": "Expense",
                  "categoryId": "0000000a-0000-4000-8000-000000000026",
                  "currencyCode": "USD",
                  "accountId": "33333333-3333-4333-8333-333333333333"
                }
              ]
            }
            """);

        await Transfer(db).ImportJsonAsync(path, ImportMode.Merge, CancellationToken.None);

        var restored = (await db.Entries.GetAsync(new EntryFilter(), CancellationToken.None)).Single();
        Assert.Null(restored.AccountId);
    }

    [Fact]
    public async Task Csv_export_has_a_stable_header_and_iso_dates()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 9), 42.5m, EntryKind.Expense, DefaultCategories.Groceries, "Milk, eggs");
        var path = db.TempFile("export.csv");

        await Transfer(db).ExportCsvAsync(path, CancellationToken.None);
        var lines = await File.ReadAllLinesAsync(path);

        Assert.Equal("Date,Kind,Category,Amount,Currency,Account,AccountLast4,Note", lines[0]);
        // The note contains a comma, so it must come back quoted.
        Assert.Equal("2026-08-09,Expense,Groceries,42.5,USD,,,\"Milk, eggs\"", lines[1]);
    }

    [Fact]
    public async Task Csv_import_creates_categories_it_has_never_seen()
    {
        using var db = new TestDatabase(Today);
        var path = db.TempFile("import.csv");
        await File.WriteAllTextAsync(
            path,
            "Date,Kind,Category,Amount,Currency,Account,AccountLast4,Note\n" +
            "2026-08-02,Expense,Childcare,320,USD,,,Daycare\n" +
            "2026-08-03,Income,Salary,1200,USD,,,\n");

        var result = await Transfer(db).ImportCsvAsync(path, ImportMode.Merge, CancellationToken.None);

        Assert.Equal(2, result.EntriesImported);
        Assert.Equal(1, result.CategoriesImported);
        var categories = await db.Categories.GetAllAsync(CancellationToken.None);
        var childcare = categories.Single(c => c.Name == "Childcare");
        Assert.Equal(EntryKind.Expense, childcare.Kind);
        Assert.False(childcare.IsSystem);
    }

    [Fact]
    public async Task Csv_import_keeps_card_details_on_the_entry()
    {
        using var db = new TestDatabase(Today);
        var path = db.TempFile("cards.csv");
        await File.WriteAllTextAsync(
            path,
            "Date,Kind,Category,Amount,Currency,Account,AccountLast4,Note\n" +
            "2026-08-12,Expense,Credit card,480,USD,Sapphire Visa,4417,Statement\n");

        await Transfer(db).ImportCsvAsync(path, ImportMode.Merge, CancellationToken.None);

        var entry = (await db.Entries.GetAsync(new EntryFilter(), CancellationToken.None)).Single();
        Assert.Equal("Sapphire Visa", entry.AccountName);
        Assert.Equal("4417", entry.AccountLast4);
        Assert.Equal(DefaultCategories.CreditCard, entry.CategoryId);
    }

    [Fact]
    public async Task Csv_import_rejects_a_file_without_the_required_columns()
    {
        using var db = new TestDatabase(Today);
        var path = db.TempFile("bad.csv");
        await File.WriteAllTextAsync(path, "When,HowMuch\n2026-08-01,12\n");

        var ex = await Assert.ThrowsAsync<ImportFormatException>(
            () => Transfer(db).ImportCsvAsync(path, ImportMode.Merge, CancellationToken.None));

        Assert.Contains("Date", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_import_rejects_a_file_that_is_not_a_backup()
    {
        using var db = new TestDatabase(Today);
        var path = db.TempFile("bad.json");
        await File.WriteAllTextAsync(path, "{ not json at all ");

        await Assert.ThrowsAsync<ImportFormatException>(
            () => Transfer(db).ImportJsonAsync(path, ImportMode.Merge, CancellationToken.None));
    }
}
