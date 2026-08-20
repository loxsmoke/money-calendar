using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.Tests.Data;

public class RepositoryTests
{
    private static readonly DateOnly Today = new(2026, 8, 18);

    [Fact]
    public async Task A_new_database_holds_the_built_in_categories_and_nothing_else()
    {
        // A first launch opens on an empty ledger: no invented accounts, no invented entries.
        using var db = new TestDatabase(Today);

        Assert.Equal(0, await db.Entries.CountAsync(CancellationToken.None));
        Assert.Empty(await db.Accounts.GetAllAsync(CancellationToken.None));
        var categories = await db.Categories.GetAllAsync(CancellationToken.None);
        Assert.NotEmpty(categories);
        Assert.All(categories, c => Assert.True(c.IsSystem));
    }

    [Fact]
    public async Task Bootstrapper_seeds_every_built_in_category()
    {
        using var db = new TestDatabase(Today);

        var categories = await db.Categories.GetAllAsync(CancellationToken.None);

        Assert.Equal(DefaultCategories.All.Count, categories.Count);
        Assert.Contains(categories, c => c.Name == "Salary" && c.Kind == EntryKind.Income);
        Assert.Contains(categories, c => c.Name == "Credit card" && c.WantsAccountDetails);
    }

    [Fact]
    public async Task Amounts_are_stored_as_positive_magnitudes()
    {
        using var db = new TestDatabase(Today);

        var entry = await db.AddAsync(new DateOnly(2026, 8, 4), -55.25m, EntryKind.Expense, DefaultCategories.Fee);

        var stored = await db.Entries.GetByIdAsync(entry.Id, CancellationToken.None);
        Assert.Equal(55.25m, stored!.Amount);
        Assert.Equal(-55.25m, stored.SignedAmount);
    }

    [Fact]
    public async Task Entries_come_back_newest_first()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 1), 10m, EntryKind.Expense, DefaultCategories.Fee);
        await db.AddAsync(new DateOnly(2026, 8, 9), 20m, EntryKind.Expense, DefaultCategories.Fee);
        await db.AddAsync(new DateOnly(2026, 8, 5), 30m, EntryKind.Expense, DefaultCategories.Fee);

        var rows = await db.Entries.GetAsync(new EntryFilter(), CancellationToken.None);

        Assert.Equal([new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 1)],
            rows.Select(r => r.Date));
    }

    [Fact]
    public async Task Kind_and_day_filters_narrow_the_list()
    {
        using var db = new TestDatabase(Today);
        await db.AddAsync(new DateOnly(2026, 8, 5), 100m, EntryKind.Income, DefaultCategories.Tips);
        await db.AddAsync(new DateOnly(2026, 8, 5), 40m, EntryKind.Expense, DefaultCategories.Transport);
        await db.AddAsync(new DateOnly(2026, 8, 6), 40m, EntryKind.Expense, DefaultCategories.Transport);

        var income = await db.Entries.GetAsync(new EntryFilter(Kind: EntryKind.Income), CancellationToken.None);
        var fifth = await db.Entries.GetAsync(new EntryFilter(Day: new DateOnly(2026, 8, 5)), CancellationToken.None);

        Assert.Single(income);
        Assert.Equal(2, fifth.Count);
    }

    [Fact]
    public async Task Search_matches_note_category_and_card_digits()
    {
        using var db = new TestDatabase(Today);
        await db.Entries.AddAsync(
            new Entry
            {
                Date = new DateOnly(2026, 8, 12),
                Amount = 480m,
                Kind = EntryKind.Expense,
                CategoryId = DefaultCategories.CreditCard,
                CurrencyCode = "USD",
                AccountName = "Sapphire Visa",
                AccountLast4 = "4417",
                Note = "Statement payment",
            },
            CancellationToken.None);
        await db.AddAsync(new DateOnly(2026, 8, 13), 20m, EntryKind.Expense, DefaultCategories.Fee, "Wire fee");

        Assert.Single(await db.Entries.GetAsync(new EntryFilter(Search: "4417"), CancellationToken.None));
        Assert.Single(await db.Entries.GetAsync(new EntryFilter(Search: "sapphire"), CancellationToken.None));
        Assert.Single(await db.Entries.GetAsync(new EntryFilter(Search: "Credit"), CancellationToken.None));
        Assert.Equal(2, (await db.Entries.GetAsync(new EntryFilter(Search: "e"), CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Update_replaces_the_editable_fields()
    {
        using var db = new TestDatabase(Today);
        var entry = await db.AddAsync(new DateOnly(2026, 8, 4), 100m, EntryKind.Expense, DefaultCategories.Fee);

        entry.Amount = 125m;
        entry.CategoryId = DefaultCategories.Utilities;
        entry.Note = "Water";
        await db.Entries.UpdateAsync(entry, CancellationToken.None);

        var stored = await db.Entries.GetByIdAsync(entry.Id, CancellationToken.None);
        Assert.Equal(125m, stored!.Amount);
        Assert.Equal(DefaultCategories.Utilities, stored.CategoryId);
        Assert.Equal("Water", stored.Note);
    }

    [Fact]
    public async Task Delete_removes_only_the_targeted_entry()
    {
        using var db = new TestDatabase(Today);
        var first = await db.AddAsync(new DateOnly(2026, 8, 4), 10m, EntryKind.Expense, DefaultCategories.Fee);
        await db.AddAsync(new DateOnly(2026, 8, 5), 20m, EntryKind.Expense, DefaultCategories.Fee);

        await db.Entries.DeleteAsync(first.Id, CancellationToken.None);

        Assert.Equal(1, await db.Entries.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task System_categories_cannot_be_deleted_and_used_ones_are_protected()
    {
        using var db = new TestDatabase(Today);
        var custom = await db.Categories.AddAsync(
            new Category { Name = "Childcare", Kind = EntryKind.Expense, ColorHex = "#D4636F" },
            CancellationToken.None);
        await db.AddAsync(new DateOnly(2026, 8, 4), 320m, EntryKind.Expense, custom.Id);

        Assert.False(await db.Categories.TryDeleteAsync(DefaultCategories.Rent, CancellationToken.None));
        Assert.False(await db.Categories.TryDeleteAsync(custom.Id, CancellationToken.None));

        await db.Entries.DeleteAllAsync(CancellationToken.None);
        Assert.True(await db.Categories.TryDeleteAsync(custom.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AddMissing_skips_ids_and_names_that_already_exist()
    {
        using var db = new TestDatabase(Today);

        var added = await db.Categories.AddMissingAsync(
            [
                new Category { Id = DefaultCategories.Rent, Name = "Rent", Kind = EntryKind.Expense, ColorHex = "#000000" },
                new Category { Name = "Rent", Kind = EntryKind.Expense, ColorHex = "#000000" },
                new Category { Name = "Bonus", Kind = EntryKind.Income, ColorHex = "#4FA3A5" },
            ],
            CancellationToken.None);

        Assert.Equal(1, added);
        var categories = await db.Categories.GetAllAsync(CancellationToken.None);
        Assert.Single(categories, c => c.Name == "Bonus");
        Assert.Single(categories, c => c.Name == "Rent");
    }
}
