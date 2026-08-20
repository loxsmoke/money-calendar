using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.Core.Services;

/// <summary>
/// JSON backup envelope. Version bumps whenever the shape changes; <c>Accounts</c> arrived
/// after v1 and is optional, so older backups still import.
/// </summary>
public sealed record BackupFile(
    int Version,
    DateTimeOffset ExportedAt,
    IReadOnlyList<BackupCategory> Categories,
    IReadOnlyList<BackupEntry> Entries,
    IReadOnlyList<BackupAccount>? Accounts = null);

public sealed record BackupAccount(Guid Id, string Name, AccountType Type, string? Last4, string? Note);

public sealed record BackupCategory(
    Guid Id, string Name, EntryKind Kind, string ColorHex, bool IsSystem, bool WantsAccountDetails, int SortOrder);

public sealed record BackupEntry(
    Guid Id, DateOnly Date, decimal Amount, EntryKind Kind, Guid CategoryId, string CurrencyCode,
    string? AccountName, string? AccountLast4, string? Note,
    Guid? AccountId = null, Guid? ToAccountId = null,
    RecurrenceFrequency Frequency = RecurrenceFrequency.None,
    int? DayOfMonth = null, int? SecondDayOfMonth = null,
    MonthDayMode SecondDayMode = MonthDayMode.OnDay,
    DayOfWeek? Weekday = null, DateOnly? RecurrenceEnd = null);

/// <summary>Thrown when an import file cannot be read as a money-calendar export.</summary>
public sealed class ImportFormatException(string message) : Exception(message);

/// <inheritdoc cref="IDataTransferService"/>
public sealed class DataTransferService(
    IEntryRepository entries,
    ICategoryRepository categories,
    IAccountRepository accounts,
    IClock clock) : IDataTransferService
{
    public const int CurrentVersion = 1;
    private const string CsvHeader = "Date,Kind,Category,Amount,Currency,Account,AccountLast4,Note";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Pinned so backups are byte-identical across platforms (golden-file testable).
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<int> ExportJsonAsync(string filePath, CancellationToken ct)
    {
        var allCategories = await categories.GetAllAsync(ct).ConfigureAwait(false);
        var allEntries = await entries.GetAsync(new EntryFilter(), ct).ConfigureAwait(false);

        var backup = new BackupFile(
            CurrentVersion,
            clock.UtcNow,
            allCategories
                .OrderBy(c => c.Kind)
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .Select(c => new BackupCategory(
                    c.Id, c.Name, c.Kind, c.ColorHex, c.IsSystem, c.WantsAccountDetails, c.SortOrder))
                .ToList(),
            allEntries
                .OrderBy(e => e.Date)
                .ThenBy(e => e.Id)
                .Select(e => new BackupEntry(
                    e.Id, e.Date, e.Amount, e.Kind, e.CategoryId, e.CurrencyCode,
                    e.AccountName, e.AccountLast4, e.Note,
                    e.AccountId, e.ToAccountId, e.Frequency, e.DayOfMonth, e.SecondDayOfMonth,
                    e.SecondDayMode, e.Weekday, e.RecurrenceEnd))
                .ToList(),
            (await accounts.GetAllAsync(ct).ConfigureAwait(false))
                .OrderBy(a => a.Type)
                .ThenBy(a => a.Name, StringComparer.Ordinal)
                .Select(a => new BackupAccount(a.Id, a.Name, a.Type, a.Last4, a.Note))
                .ToList());

        WriteDirectory(filePath);
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(backup, JsonOptions), ct)
            .ConfigureAwait(false);
        return backup.Entries.Count;
    }

    public async Task<int> ExportCsvAsync(string filePath, CancellationToken ct)
    {
        var index = (await categories.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(c => c.Id);
        var rows = (await entries.GetAsync(new EntryFilter(), ct).ConfigureAwait(false))
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToList();

        var text = new StringBuilder();
        text.Append(CsvHeader).Append('\n');
        foreach (var entry in rows)
        {
            string[] fields =
            [
                entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                entry.Kind.ToString(),
                index.GetValueOrDefault(entry.CategoryId)?.Name ?? "Other",
                CsvText.Money(entry.Amount),
                entry.CurrencyCode,
                entry.AccountName ?? "",
                entry.AccountLast4 ?? "",
                entry.Note ?? "",
            ];
            text.Append(string.Join(",", fields.Select(CsvText.Escape))).Append('\n');
        }

        WriteDirectory(filePath);
        await File.WriteAllTextAsync(filePath, text.ToString(), ct).ConfigureAwait(false);
        return rows.Count;
    }

    public async Task<ImportResult> ImportJsonAsync(string filePath, ImportMode mode, CancellationToken ct)
    {
        BackupFile? backup;
        try
        {
            await using var stream = File.OpenRead(filePath);
            backup = await JsonSerializer.DeserializeAsync<BackupFile>(stream, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new ImportFormatException("The file is not valid JSON: " + ex.Message);
        }

        if (backup?.Entries is null || backup.Categories is null)
            throw new ImportFormatException("The file is not a money-calendar backup.");
        if (backup.Version > CurrentVersion)
            throw new ImportFormatException(
                $"The backup was written by a newer version (v{backup.Version}); this app reads v{CurrentVersion}.");

        var importedCategories = backup.Categories
            .Select(c => new Category
            {
                Id = c.Id,
                Name = c.Name,
                Kind = c.Kind,
                ColorHex = string.IsNullOrWhiteSpace(c.ColorHex) ? "#8A8A92" : c.ColorHex,
                IsSystem = c.IsSystem,
                WantsAccountDetails = c.WantsAccountDetails,
                SortOrder = c.SortOrder,
            })
            .ToList();
        var categoriesAdded = await categories.AddMissingAsync(importedCategories, ct).ConfigureAwait(false);

        if (backup.Accounts is { Count: > 0 } backupAccounts)
        {
            await accounts.AddMissingAsync(
                backupAccounts.Select(a => new Account
                {
                    Id = a.Id,
                    Name = a.Name,
                    Type = a.Type,
                    Last4 = a.Last4,
                    Note = a.Note,
                }),
                ct).ConfigureAwait(false);
        }

        var known = (await categories.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(c => c.Id);
        var knownAccounts = (await accounts.GetAllAsync(ct).ConfigureAwait(false))
            .Select(a => a.Id)
            .ToHashSet();
        var stamp = clock.UtcNow;
        var incoming = backup.Entries
            .Select(e => new Entry
            {
                Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id,
                Date = e.Date,
                Amount = Math.Abs(e.Amount),
                Kind = e.Kind,
                CategoryId = known.ContainsKey(e.CategoryId) ? e.CategoryId : FallbackCategory(e.Kind),
                CurrencyCode = string.IsNullOrWhiteSpace(e.CurrencyCode) ? "USD" : e.CurrencyCode,
                // Account links survive only when the backup carried the accounts too.
                AccountId = knownAccounts.Contains(e.AccountId ?? Guid.Empty) ? e.AccountId : null,
                ToAccountId = knownAccounts.Contains(e.ToAccountId ?? Guid.Empty) ? e.ToAccountId : null,
                AccountName = e.AccountName,
                AccountLast4 = e.AccountLast4,
                Note = e.Note,
                Frequency = e.Frequency,
                DayOfMonth = e.DayOfMonth,
                SecondDayOfMonth = e.SecondDayOfMonth,
                SecondDayMode = e.SecondDayMode,
                Weekday = e.Weekday,
                RecurrenceEnd = e.RecurrenceEnd,
                CreatedAt = stamp,
                UpdatedAt = stamp,
            })
            .ToList();

        return await ApplyAsync(incoming, mode, categoriesAdded, ct).ConfigureAwait(false);
    }

    public async Task<ImportResult> ImportCsvAsync(string filePath, ImportMode mode, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
        if (lines.Length == 0)
            throw new ImportFormatException("The file is empty.");

        var columns = CsvText.SplitLine(lines[0])
            .Select((name, i) => (Name: name.Trim(), Index: i))
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "Date", "Kind", "Category", "Amount" })
        {
            if (!columns.ContainsKey(required))
                throw new ImportFormatException($"The CSV is missing the required '{required}' column.");
        }

        var byName = (await categories.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(c => (c.Kind, c.Name), CategoryKeyComparer.Instance);
        var created = new List<Category>();
        var stamp = clock.UtcNow;
        var incoming = new List<Entry>();

        for (var line = 1; line < lines.Length; line++)
        {
            if (string.IsNullOrWhiteSpace(lines[line]))
                continue;

            var fields = CsvText.SplitLine(lines[line]);
            string Field(string name) =>
                columns.TryGetValue(name, out var i) && i < fields.Count ? fields[i].Trim() : "";

            var dateText = Field("Date");
            if (!DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new ImportFormatException($"Line {line + 1}: '{dateText}' is not an ISO date (yyyy-MM-dd).");

            var amountText = Field("Amount");
            if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                throw new ImportFormatException($"Line {line + 1}: '{amountText}' is not a number.");

            var kindText = Field("Kind");
            var kind = kindText.StartsWith("i", StringComparison.OrdinalIgnoreCase)
                ? EntryKind.Income
                : kindText.StartsWith("e", StringComparison.OrdinalIgnoreCase)
                    ? EntryKind.Expense
                    // Fall back to the sign when the column carries something else entirely.
                    : amount >= 0 ? EntryKind.Income : EntryKind.Expense;

            var categoryName = Field("Category");
            if (categoryName.Length == 0)
                categoryName = "Other";
            if (!byName.TryGetValue((kind, categoryName), out var category))
            {
                category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = categoryName,
                    Kind = kind,
                    ColorHex = PaletteColor(categoryName),
                    SortOrder = 500,
                };
                byName[(kind, categoryName)] = category;
                created.Add(category);
            }

            var currency = Field("Currency");
            incoming.Add(new Entry
            {
                Id = Guid.NewGuid(),
                Date = date,
                Amount = Math.Abs(amount),
                Kind = kind,
                CategoryId = category.Id,
                CurrencyCode = currency.Length == 3 ? currency.ToUpperInvariant() : "USD",
                AccountName = NullIfEmpty(Field("Account")),
                AccountLast4 = NullIfEmpty(Field("AccountLast4")),
                Note = NullIfEmpty(Field("Note")),
                CreatedAt = stamp,
                UpdatedAt = stamp,
            });
        }

        var categoriesAdded = created.Count == 0
            ? 0
            : await categories.AddMissingAsync(created, ct).ConfigureAwait(false);
        return await ApplyAsync(incoming, mode, categoriesAdded, ct).ConfigureAwait(false);
    }

    private async Task<ImportResult> ApplyAsync(
        IReadOnlyList<Entry> incoming, ImportMode mode, int categoriesAdded, CancellationToken ct)
    {
        if (mode == ImportMode.Replace)
            await entries.DeleteAllAsync(ct).ConfigureAwait(false);

        var imported = await entries.AddRangeAsync(incoming, ct).ConfigureAwait(false);
        return new ImportResult(imported, incoming.Count - imported, categoriesAdded);
    }

    private static void WriteDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static string PaletteColor(string name)
    {
        var palette = DefaultCategories.CustomPalette;
        var hash = Math.Abs(name.ToUpperInvariant().GetHashCode(StringComparison.Ordinal));
        return palette[hash % palette.Count];
    }

    private static Guid FallbackCategory(EntryKind kind) =>
        kind == EntryKind.Income ? DefaultCategories.OtherIncome : DefaultCategories.OtherExpense;

    /// <summary>Case-insensitive category lookup keyed by (kind, name).</summary>
    private sealed class CategoryKeyComparer : IEqualityComparer<(EntryKind Kind, string Name)>
    {
        public static CategoryKeyComparer Instance { get; } = new();

        public bool Equals((EntryKind Kind, string Name) x, (EntryKind Kind, string Name) y) =>
            x.Kind == y.Kind && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((EntryKind Kind, string Name) obj) =>
            HashCode.Combine(obj.Kind, obj.Name.ToUpperInvariant());
    }
}
