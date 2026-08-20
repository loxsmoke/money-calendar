using Microsoft.EntityFrameworkCore;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Data.Repositories;

/// <summary>The user's named accounts, ordered by type then name — the order the section shows.</summary>
public sealed class AccountRepository(IDbContextFactory<MoneyCalendarDbContext> contextFactory, IClock clock)
    : IAccountRepository
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Accounts.AsNoTracking()
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Account> AddAsync(Account account, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (account.Id == Guid.Empty)
            account.Id = Guid.NewGuid();
        account.CreatedAt = account.CreatedAt == default ? clock.UtcNow : account.CreatedAt;
        account.UpdatedAt = clock.UtcNow;

        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return account;
    }

    public async Task UpdateAsync(Account account, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Accounts.FirstOrDefaultAsync(a => a.Id == account.Id, ct).ConfigureAwait(false);
        if (existing is null)
            return;

        existing.Name = account.Name;
        existing.Type = account.Type;
        existing.Last4 = account.Last4;
        existing.Note = account.Note;
        existing.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.Accounts.Where(a => a.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> UsageCountAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Entries
            .CountAsync(e => e.AccountId == accountId || e.ToAccountId == accountId, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> ReassignAsync(Guid fromId, Guid toId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var stamp = clock.UtcNow;

        var moved = await db.Entries
            .Where(e => e.AccountId == fromId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.AccountId, toId).SetProperty(x => x.UpdatedAt, stamp), ct)
            .ConfigureAwait(false);
        moved += await db.Entries
            .Where(e => e.ToAccountId == fromId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.ToAccountId, toId).SetProperty(x => x.UpdatedAt, stamp), ct)
            .ConfigureAwait(false);
        return moved;
    }

    public async Task<int> AddMissingAsync(IEnumerable<Account> accounts, CancellationToken ct)
    {
        var incoming = accounts.ToList();
        if (incoming.Count == 0)
            return 0;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Accounts.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var existingIds = existing.Select(a => a.Id).ToHashSet();
        var existingNames = existing.Select(a => a.Name.ToUpperInvariant()).ToHashSet();

        var stamp = clock.UtcNow;
        var toAdd = new List<Account>();
        foreach (var account in incoming)
        {
            if (existingIds.Contains(account.Id) || existingNames.Contains(account.Name.ToUpperInvariant()))
                continue;
            if (account.Id == Guid.Empty)
                account.Id = Guid.NewGuid();
            account.CreatedAt = account.CreatedAt == default ? stamp : account.CreatedAt;
            account.UpdatedAt = stamp;
            existingIds.Add(account.Id);
            existingNames.Add(account.Name.ToUpperInvariant());
            toAdd.Add(account);
        }

        if (toAdd.Count == 0)
            return 0;

        db.Accounts.AddRange(toAdd);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return toAdd.Count;
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Accounts.ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
