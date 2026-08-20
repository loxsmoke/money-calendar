using Microsoft.EntityFrameworkCore;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Data.Repositories;

/// <summary>Income types and expense categories, ordered the way the pickers show them.</summary>
public sealed class CategoryRepository(IDbContextFactory<MoneyCalendarDbContext> contextFactory) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Categories.AsNoTracking()
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Category> AddAsync(Category category, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (category.Id == Guid.Empty)
            category.Id = Guid.NewGuid();
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return category;
    }

    public async Task UpdateAsync(Category category, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Categories.FirstOrDefaultAsync(c => c.Id == category.Id, ct).ConfigureAwait(false);
        if (existing is null)
            return;

        existing.Name = category.Name;
        existing.ColorHex = category.ColorHex;
        existing.WantsAccountDetails = category.WantsAccountDetails;
        existing.SortOrder = category.SortOrder;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> TryDeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (category is null || category.IsSystem)
            return false;
        if (await db.Entries.AnyAsync(e => e.CategoryId == id, ct).ConfigureAwait(false))
            return false;

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<int> AddMissingAsync(IEnumerable<Category> categories, CancellationToken ct)
    {
        var incoming = categories.ToList();
        if (incoming.Count == 0)
            return 0;

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Categories.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var existingIds = existing.Select(c => c.Id).ToHashSet();
        var existingNames = existing
            .Select(c => (c.Kind, Name: c.Name.ToUpperInvariant()))
            .ToHashSet();

        var toAdd = new List<Category>();
        foreach (var category in incoming)
        {
            var nameKey = (category.Kind, Name: category.Name.ToUpperInvariant());
            if (existingIds.Contains(category.Id) || existingNames.Contains(nameKey))
                continue;
            if (category.Id == Guid.Empty)
                category.Id = Guid.NewGuid();
            existingIds.Add(category.Id);
            existingNames.Add(nameKey);
            toAdd.Add(category);
        }

        if (toAdd.Count == 0)
            return 0;

        db.Categories.AddRange(toAdd);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return toAdd.Count;
    }
}
