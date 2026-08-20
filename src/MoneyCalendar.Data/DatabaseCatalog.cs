using Microsoft.Data.Sqlite;

namespace MoneyCalendar.Data;

/// <summary>One database file in the data folder, as the Developer section lists them.</summary>
public sealed record DatabaseFile(string Name, string Path, long SizeBytes, DateTimeOffset ModifiedAt);

/// <summary>
/// The database files the app can open, and the operations the Developer section offers over
/// them. Every database is a plain SQLite file in one folder — the app's data folder — so this
/// is file management with a little care taken over the file that is currently open.
/// </summary>
public interface IDatabaseCatalog
{
    /// <summary>Where the files live: the folder holding the database the app opened with.</summary>
    string Directory { get; }

    /// <summary>The database the app is reading and writing right now.</summary>
    string CurrentName { get; }

    /// <summary>Every database in the folder, by name.</summary>
    IReadOnlyList<DatabaseFile> List();

    /// <summary>Creates an empty database, schema and built-in categories included.</summary>
    Task<string> CreateAsync(string name, CancellationToken ct);

    /// <summary>Copies an existing database under a new name. The copy is not switched to.</summary>
    Task<string> CloneAsync(string source, string name, CancellationToken ct);

    /// <summary>Points the app at another database. Callers reload their pages afterwards.</summary>
    Task SwitchToAsync(string name, CancellationToken ct);

    /// <summary>Renames a database, the open one included.</summary>
    string Rename(string name, string newName);

    /// <summary>Deletes a database. The open one is refused.</summary>
    void Delete(string name);
}

/// <inheritdoc cref="IDatabaseCatalog"/>
public sealed class DatabaseCatalog(
    MoneyCalendarDataOptions options,
    DatabaseBootstrapper bootstrapper) : IDatabaseCatalog
{
    public const string Extension = ".db";

    /// <summary>Long enough for a sentence of a name, short enough to stay a file name.</summary>
    public const int MaxNameLength = 60;

    public string Directory =>
        Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath)) ?? ".";

    public string CurrentName => Path.GetFileNameWithoutExtension(options.DatabasePath);

    public IReadOnlyList<DatabaseFile> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];

        return System.IO.Directory.EnumerateFiles(Directory, "*" + Extension)
            .Select(path => new FileInfo(path))
            .Select(file => new DatabaseFile(
                Path.GetFileNameWithoutExtension(file.Name),
                file.FullName,
                file.Length,
                file.LastWriteTimeUtc))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<string> CreateAsync(string name, CancellationToken ct)
    {
        var path = PathForNew(name);
        // Never seeded: a database you asked for should open on an empty ledger.
        await bootstrapper.InitializeAsync(path, seedSampleData: false, ct).ConfigureAwait(false);
        return path;
    }

    public async Task<string> CloneAsync(string source, string name, CancellationToken ct)
    {
        var from = PathForExisting(source);
        var to = PathForNew(name);

        // Copying the open file means copying whatever SQLite is still holding, so let go of
        // the handles first. Nothing else is mid-write: the UI runs one command at a time.
        SqliteConnection.ClearAllPools();
        File.Copy(from, to);

        // A clone made by an older build may be missing later columns; opening it settles that.
        await bootstrapper.InitializeAsync(to, seedSampleData: false, ct).ConfigureAwait(false);
        return to;
    }

    public async Task SwitchToAsync(string name, CancellationToken ct)
    {
        var path = PathForExisting(name);
        if (string.Equals(path, Path.GetFullPath(options.DatabasePath), StringComparison.OrdinalIgnoreCase))
            return;

        SqliteConnection.ClearAllPools();
        options.DatabasePath = path;
        await bootstrapper.InitializeAsync(ct).ConfigureAwait(false);
    }

    public string Rename(string name, string newName)
    {
        var from = PathForExisting(name);
        var to = PathForNew(newName);
        var isOpen = string.Equals(from, Path.GetFullPath(options.DatabasePath), StringComparison.OrdinalIgnoreCase);

        SqliteConnection.ClearAllPools();
        File.Move(from, to);
        foreach (var (fromSide, toSide) in SideFiles(from).Zip(SideFiles(to)))
        {
            if (File.Exists(fromSide))
                File.Move(fromSide, toSide, overwrite: true);
        }

        if (isOpen)
            options.DatabasePath = to;
        return to;
    }

    public void Delete(string name)
    {
        var path = PathForExisting(name);
        if (string.Equals(path, Path.GetFullPath(options.DatabasePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{name}' is the database in use. Switch to another one first, then delete it.");
        }

        SqliteConnection.ClearAllPools();
        File.Delete(path);
        foreach (var side in SideFiles(path).Where(File.Exists))
            File.Delete(side);
    }

    // ---- names -------------------------------------------------------------

    /// <summary>
    /// A database name is a file name, so it has to survive being one. The rules are reported
    /// as messages rather than swallowed: this is the Developer section, not a wizard.
    /// </summary>
    public static string Validate(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Give the database a name.");
        if (trimmed.Length > MaxNameLength)
            throw new InvalidOperationException($"Keep the name to {MaxNameLength} characters or fewer.");
        if (trimmed.EndsWith('.') || trimmed.StartsWith('.'))
            throw new InvalidOperationException("A name cannot start or end with a dot.");
        if (trimmed.IndexOfAny([.. Path.GetInvalidFileNameChars(), Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new InvalidOperationException(@"A name cannot contain \ / : * ? "" < > |.");
        return trimmed;
    }

    private string PathForNew(string name)
    {
        var path = Path.Combine(Directory, Validate(name) + Extension);
        if (File.Exists(path))
            throw new InvalidOperationException($"'{Validate(name)}' already exists.");
        return path;
    }

    private string PathForExisting(string name)
    {
        var path = Path.GetFullPath(Path.Combine(Directory, Validate(name) + Extension));
        if (!File.Exists(path))
            throw new InvalidOperationException($"'{Validate(name)}' is no longer there.");
        return path;
    }

    /// <summary>The write-ahead and shared-memory files SQLite may leave beside a database.</summary>
    private static IEnumerable<string> SideFiles(string path) => [path + "-wal", path + "-shm"];
}
