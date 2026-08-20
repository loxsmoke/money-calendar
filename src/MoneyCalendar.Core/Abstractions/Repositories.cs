using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Abstractions;

/// <summary>Inclusive date window. Every query in the app is scoped by one.</summary>
public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public int DayCount => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= From && date <= To;

    public static DateRange Month(int year, int month) => new(
        new DateOnly(year, month, 1),
        new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

    public IEnumerable<DateOnly> Days()
    {
        for (var day = From; day <= To; day = day.AddDays(1))
            yield return day;
    }
}

/// <summary>Entry list filter. Null members mean "no restriction".</summary>
public sealed record EntryFilter(
    DateRange? Range = null,
    EntryKind? Kind = null,
    IReadOnlyCollection<Guid>? CategoryIds = null,
    DateOnly? Day = null,
    string? Search = null);

public interface IEntryRepository
{
    Task<IReadOnlyList<Entry>> GetAsync(EntryFilter filter, CancellationToken ct);
    Task<Entry?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Entry> AddAsync(Entry entry, CancellationToken ct);
    Task UpdateAsync(Entry entry, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Bulk insert used by import and sample data. Existing ids are skipped.</summary>
    Task<int> AddRangeAsync(IEnumerable<Entry> entries, CancellationToken ct);

    Task<int> DeleteAllAsync(CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
}

public interface IAccountRepository
{
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct);
    Task<Account> AddAsync(Account account, CancellationToken ct);
    Task UpdateAsync(Account account, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>How many stored entries point at this account, from either end.</summary>
    Task<int> UsageCountAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Repoints every entry that uses <paramref name="fromId"/> at <paramref name="toId"/>,
    /// on whichever end it was used. Returns the number of entries changed.
    /// </summary>
    Task<int> ReassignAsync(Guid fromId, Guid toId, CancellationToken ct);

    /// <summary>Bulk insert used by import. Existing ids and duplicate names are skipped.</summary>
    Task<int> AddMissingAsync(IEnumerable<Account> accounts, CancellationToken ct);

    /// <summary>Drops every account. Entries that pointed at one are left without it.</summary>
    Task<int> DeleteAllAsync(CancellationToken ct);
}

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct);
    Task<Category> AddAsync(Category category, CancellationToken ct);
    Task UpdateAsync(Category category, CancellationToken ct);

    /// <summary>Deletes a user category. Returns false when it is a system category or still in use.</summary>
    Task<bool> TryDeleteAsync(Guid id, CancellationToken ct);

    Task<int> AddMissingAsync(IEnumerable<Category> categories, CancellationToken ct);
}
