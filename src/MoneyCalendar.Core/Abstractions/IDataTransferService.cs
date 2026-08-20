namespace MoneyCalendar.Core.Abstractions;

/// <summary>How an import treats data that is already in the database.</summary>
public enum ImportMode
{
    /// <summary>Keep existing rows; add entries whose id is not present yet.</summary>
    Merge = 0,

    /// <summary>Delete every entry first, then insert the file's entries.</summary>
    Replace = 1,
}

public sealed record ImportResult(int EntriesImported, int EntriesSkipped, int CategoriesImported)
{
    public static ImportResult Empty { get; } = new(0, 0, 0);
}

/// <summary>
/// Settings → Data: a full JSON backup that round-trips categories and entries, plus a flat
/// CSV for spreadsheets. Output is deterministic (invariant culture, ISO dates, stable
/// ordering, \n endings) so the formats are golden-file testable.
/// </summary>
public interface IDataTransferService
{
    Task<int> ExportJsonAsync(string filePath, CancellationToken ct);
    Task<int> ExportCsvAsync(string filePath, CancellationToken ct);
    Task<ImportResult> ImportJsonAsync(string filePath, ImportMode mode, CancellationToken ct);
    Task<ImportResult> ImportCsvAsync(string filePath, ImportMode mode, CancellationToken ct);
}
