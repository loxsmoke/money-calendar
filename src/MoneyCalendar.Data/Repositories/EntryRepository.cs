using Microsoft.EntityFrameworkCore;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Data.Repositories;

/// <summary>
/// Entry CRUD. Every read returns rows ordered newest first with the category eager-loaded,
/// which is what every list in the UI wants.
/// </summary>
public sealed class EntryRepository(IDbContextFactory<MoneyCalendarDbContext> contextFactory, IClock clock)
    : IEntryRepository
{
    public async Task<IReadOnlyList<Entry>> GetAsync(EntryFilter filter, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<Entry> query = db.Entries.AsNoTracking()
            .Include(e => e.Category).Include(e => e.Account).Include(e => e.ToAccount);

        if (filter.Day is { } day)
        {
            query = query.Where(e => e.Date == day);
        }
        else if (filter.Range is { } range)
        {
            query = query.Where(e => e.Date >= range.From && e.Date <= range.To);
        }

        if (filter.Kind is { } kind)
            query = query.Where(e => e.Kind == kind);

        if (filter.CategoryIds is { Count: > 0 } categoryIds)
        {
            var ids = categoryIds.ToArray();
            query = query.Where(e => ids.Contains(e.CategoryId));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e =>
                (e.Note != null && EF.Functions.Like(e.Note, "%" + term + "%"))
                || (e.AccountName != null && EF.Functions.Like(e.AccountName, "%" + term + "%"))
                || (e.AccountLast4 != null && EF.Functions.Like(e.AccountLast4, "%" + term + "%"))
                || (e.Category != null && EF.Functions.Like(e.Category.Name, "%" + term + "%"))
                || (e.Account != null && EF.Functions.Like(e.Account.Name, "%" + term + "%"))
                || (e.ToAccount != null && EF.Functions.Like(e.ToAccount.Name, "%" + term + "%")));
        }

        return await query
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Entry?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Entries.AsNoTracking()
            .Include(e => e.Category).Include(e => e.Account).Include(e => e.ToAccount)
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<Entry> AddAsync(Entry entry, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (entry.Id == Guid.Empty)
            entry.Id = Guid.NewGuid();
        entry.Amount = Math.Abs(entry.Amount);
        entry.CreatedAt = entry.CreatedAt == default ? clock.UtcNow : entry.CreatedAt;
        entry.UpdatedAt = clock.UtcNow;
        entry.Category = null;
        entry.Account = null;
        entry.ToAccount = null;

        db.Entries.Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entry;
    }

    public async Task UpdateAsync(Entry entry, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Entries.FirstOrDefaultAsync(e => e.Id == entry.Id, ct).ConfigureAwait(false);
        if (existing is null)
            return;

        existing.Date = entry.Date;
        existing.Amount = Math.Abs(entry.Amount);
        existing.Kind = entry.Kind;
        existing.CategoryId = entry.CategoryId;
        existing.AccountId = entry.AccountId;
        existing.ToAccountId = entry.ToAccountId;
        existing.CurrencyCode = entry.CurrencyCode;
        existing.AccountName = entry.AccountName;
        existing.AccountLast4 = entry.AccountLast4;
        existing.Note = entry.Note;
        existing.Frequency = entry.Frequency;
        existing.DayOfMonth = entry.DayOfMonth;
        existing.SecondDayOfMonth = entry.SecondDayOfMonth;
        existing.SecondDayMode = entry.SecondDayMode;
        existing.Weekday = entry.Weekday;
        existing.RecurrenceEnd = entry.RecurrenceEnd;
        existing.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.Entries.Where(e => e.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> AddRangeAsync(IEnumerable<Entry> entries, CancellationToken ct)
    {
        var incoming = entries.ToList();
        if (incoming.Count == 0)
            return 0;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var ids = incoming.Select(e => e.Id).ToArray();
        var existing = await db.Entries.Where(e => ids.Contains(e.Id))
            .Select(e => e.Id).ToHashSetAsync(ct).ConfigureAwait(false);

        var stamp = clock.UtcNow;
        var toAdd = incoming.Where(e => !existing.Contains(e.Id)).ToList();
        foreach (var entry in toAdd)
        {
            if (entry.Id == Guid.Empty)
                entry.Id = Guid.NewGuid();
            entry.Amount = Math.Abs(entry.Amount);
            entry.CreatedAt = entry.CreatedAt == default ? stamp : entry.CreatedAt;
            entry.UpdatedAt = stamp;
            entry.Category = null;
            entry.Account = null;
            entry.ToAccount = null;
        }

        db.Entries.AddRange(toAdd);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return toAdd.Count;
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Entries.ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Entries.CountAsync(ct).ConfigureAwait(false);
    }
}
