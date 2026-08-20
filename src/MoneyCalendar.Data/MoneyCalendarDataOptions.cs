namespace MoneyCalendar.Data;

/// <summary>Data-layer configuration. Defaults target the platform app-data path.</summary>
public sealed class MoneyCalendarDataOptions
{
    public static string DefaultAppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MoneyCalendar");

    /// <summary>Full path of the SQLite database file.</summary>
    public string DatabasePath { get; set; } = Path.Combine(DefaultAppDataDirectory, "money-calendar.db");

    /// <summary>Seed the demo ledger when the database is created empty.</summary>
    public bool SeedSampleDataOnFirstRun { get; set; } = true;
}
