using System.Text.Json;

namespace MoneyCalendar.App.Services;

/// <summary>App-local preferences. Financial data lives in the database, never here.</summary>
public sealed record AppSettings
{
    /// <summary>ISO 4217 code used for every amount in the prototype (single-currency).</summary>
    public string CurrencyCode { get; init; } = "USD";

    /// <summary>"Default" follows the OS light/dark preference.</summary>
    public string Theme { get; init; } = "Default";

    /// <summary>
    /// Which database in the data folder the app opens, without its .db extension. Switched
    /// from Settings → Developer; a name that no longer exists falls back to the default.
    /// </summary>
    public string DatabaseName { get; init; } = "money-calendar";

    /// <summary>
    /// Whether the About section asks GitHub for the latest release. On by default; turning it
    /// off means the app makes no network requests at all.
    /// </summary>
    public bool CheckForUpdates { get; init; } = true;
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    event Action? Changed;
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}

/// <summary>JSON file store at the platform app-data path.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public AppSettings Current { get; private set; }
    public event Action? Changed;

    public JsonSettingsStore(string path)
    {
        _path = path;
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Settings file unreadable; using defaults");
        }

        return new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        Current = settings;
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, Options), ct);
        Changed?.Invoke();
    }
}

/// <summary>In-memory store used by tests and headless runs.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();
    public event Action? Changed;

    public Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        Current = settings;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
