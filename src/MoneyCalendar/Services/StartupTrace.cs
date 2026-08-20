using System.Globalization;

namespace MoneyCalendar.Services;

/// <summary>
/// Writes a plain-text trace before the logging pipeline exists, so a crash during startup
/// still leaves something to read at %APPDATA%/MoneyCalendar/logs/startup-trace.log.
/// </summary>
public static class StartupTrace
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MoneyCalendar", "logs", "startup-trace.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var stamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
                var line = exception is null ? $"{stamp} {message}" : $"{stamp} {message}: {exception}";
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // Tracing must never take the app down.
        }
    }
}
