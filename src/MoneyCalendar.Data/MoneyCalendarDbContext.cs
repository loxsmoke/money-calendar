using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Data;

/// <summary>
/// EF Core context for the local SQLite database.
///
/// Storage conventions (same as the budget app, minus the SQLCipher layer):
/// - decimal columns are stored as TEXT, which round-trips full decimal precision. SQLite
///   cannot compare or aggregate decimal TEXT, so nothing sums amounts in SQL — the ranges
///   this app queries are at most three months, so aggregation happens in memory.
/// - DateTimeOffset is stored as Unix epoch milliseconds (UTC, long) so comparisons translate.
/// - DateOnly uses the provider's native TEXT "yyyy-MM-dd" storage, which sorts correctly and
///   supports Year/Month/Day member translation.
/// </summary>
public class MoneyCalendarDbContext(DbContextOptions<MoneyCalendarDbContext> options) : DbContext(options)
{
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Account> Accounts => Set<Account>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTimeOffset>().HaveConversion<UnixMillisecondsConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<NullableUnixMillisecondsConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.ColorHex).HasMaxLength(9);
            e.HasIndex(c => new { c.Kind, c.Name }).IsUnique();
        });

        model.Entity<Account>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(120);
            e.Property(a => a.Last4).HasMaxLength(8);
            e.Property(a => a.Note).HasMaxLength(500);
            e.HasIndex(a => a.Name).IsUnique();
        });

        model.Entity<Entry>(e =>
        {
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
            e.Property(x => x.AccountName).HasMaxLength(120);
            e.Property(x => x.AccountLast4).HasMaxLength(8);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Ignore(x => x.SignedAmount);
            e.Ignore(x => x.IsRecurring);
            e.Ignore(x => x.IsOccurrence);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => new { x.Kind, x.Date });
            e.HasIndex(x => x.CategoryId);
            // Deleting a category that still has entries is blocked in the repository, so
            // Restrict here is a belt-and-braces guard rather than a user-visible path.
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            // Deleting an account leaves its entries in place, unlinked: the ledger is the
            // record of what happened, the account list is just naming.
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ToAccount).WithMany().HasForeignKey(x => x.ToAccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => x.ToAccountId);
            e.HasIndex(x => x.Frequency);
        });
    }

    private sealed class UnixMillisecondsConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.ToUnixTimeMilliseconds(),
        v => DateTimeOffset.FromUnixTimeMilliseconds(v));

    private sealed class NullableUnixMillisecondsConverter() : ValueConverter<DateTimeOffset?, long?>(
        v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : null,
        v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null);
}
