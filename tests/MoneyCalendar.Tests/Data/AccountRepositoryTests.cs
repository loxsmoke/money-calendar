using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Tests.Data;

public class AccountRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 8, 19);

    private static Account New(string name, AccountType type, string? last4 = null) =>
        new() { Name = name, Type = type, Last4 = last4 };

    [Fact]
    public async Task A_fresh_database_starts_without_accounts()
    {
        using var db = new TestDatabase(Today);

        Assert.Empty(await db.Accounts.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sample_data_seeds_one_account_per_common_type()
    {
        using var db = new TestDatabase(Today, seedSample: true);

        var accounts = await db.Accounts.GetAllAsync(CancellationToken.None);

        Assert.Equal(6, accounts.Count);
        Assert.Contains(accounts, a => a.Type == AccountType.Checking);
        Assert.Contains(accounts, a => a.Type == AccountType.Savings);
        // Two cards, so the demo has somewhere to show one expense paid to each.
        Assert.Contains(accounts, a => a.Type == AccountType.Credit && a.Last4 == "1111");
        Assert.Contains(accounts, a => a.Type == AccountType.Credit && a.Last4 == "2222");
        Assert.Contains(accounts, a => a.Type == AccountType.Mortgage);
        Assert.Contains(accounts, a => a.Type == AccountType.OtherExpense);
    }

    [Fact]
    public async Task Accounts_come_back_grouped_by_type_then_name()
    {
        using var db = new TestDatabase(Today);
        await db.Accounts.AddAsync(New("Zeta Savings", AccountType.Savings), CancellationToken.None);
        await db.Accounts.AddAsync(New("Alpha Savings", AccountType.Savings), CancellationToken.None);
        await db.Accounts.AddAsync(New("Blue Card", AccountType.Credit), CancellationToken.None);

        var accounts = await db.Accounts.GetAllAsync(CancellationToken.None);

        Assert.Equal(["Blue Card", "Alpha Savings", "Zeta Savings"], accounts.Select(a => a.Name));
    }

    [Fact]
    public async Task Every_account_type_round_trips()
    {
        using var db = new TestDatabase(Today);
        foreach (var type in AccountTypes.All)
            await db.Accounts.AddAsync(New(AccountTypes.Label(type), type), CancellationToken.None);

        var accounts = await db.Accounts.GetAllAsync(CancellationToken.None);

        Assert.Equal(AccountTypes.All, accounts.Select(a => a.Type));
        Assert.Equal(7, AccountTypes.All.Count);
    }

    [Fact]
    public async Task Update_replaces_name_type_and_digits()
    {
        using var db = new TestDatabase(Today);
        var account = await db.Accounts.AddAsync(New("Old Card", AccountType.Credit, "1111"), CancellationToken.None);

        account.Name = "New Card";
        account.Type = AccountType.Checking;
        account.Last4 = "2222";
        await db.Accounts.UpdateAsync(account, CancellationToken.None);

        var stored = (await db.Accounts.GetAllAsync(CancellationToken.None)).Single();
        Assert.Equal("New Card", stored.Name);
        Assert.Equal(AccountType.Checking, stored.Type);
        Assert.Equal("2222", stored.Last4);
    }

    [Fact]
    public async Task Delete_removes_only_the_targeted_account()
    {
        using var db = new TestDatabase(Today);
        var first = await db.Accounts.AddAsync(New("One", AccountType.Checking), CancellationToken.None);
        await db.Accounts.AddAsync(New("Two", AccountType.Savings), CancellationToken.None);

        await db.Accounts.DeleteAsync(first.Id, CancellationToken.None);

        Assert.Equal(["Two"], (await db.Accounts.GetAllAsync(CancellationToken.None)).Select(a => a.Name));
    }

    [Fact]
    public async Task AddMissing_skips_ids_and_names_that_already_exist()
    {
        using var db = new TestDatabase(Today);
        var existing = await db.Accounts.AddAsync(New("Everyday Checking", AccountType.Checking), CancellationToken.None);

        var added = await db.Accounts.AddMissingAsync(
            [
                new Account { Id = existing.Id, Name = "Renamed", Type = AccountType.Checking },
                new Account { Name = "everyday checking", Type = AccountType.Savings },
                new Account { Name = "Brokerage", Type = AccountType.Investment },
            ],
            CancellationToken.None);

        Assert.Equal(1, added);
        var accounts = await db.Accounts.GetAllAsync(CancellationToken.None);
        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.Name == "Brokerage");
    }
}
