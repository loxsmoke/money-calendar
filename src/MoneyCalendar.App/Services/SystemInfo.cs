using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MoneyCalendar.App.Services;

/// <summary>
/// Diagnostic facts for the About section: what the app is running on and where it keeps its
/// data. Everything here is read locally and only leaves the machine if the user copies it.
/// </summary>
public static class SystemInfo
{
    /// <summary>The running build, e.g. "0.1.0".</summary>
    public static string AppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the "+<commit sha>" the SDK appends to the informational version.
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    }

    /// <summary>
    /// The numeric version alone — major.minor.patch, e.g. "0.1.0". The window title carries
    /// this rather than <see cref="AppVersion"/>, which can pick up a prerelease suffix or a
    /// build sha that means nothing in a title bar.
    /// </summary>
    public static string NumericVersion() =>
        FormatNumericVersion(Assembly.GetExecutingAssembly().GetName().Version);

    internal static string FormatNumericVersion(Version? version) => version?.ToString(3) ?? "0.1.0";

    /// <summary>Operating system name and build, e.g. "Microsoft Windows 10.0.26200".</summary>
    public static string OperatingSystem() => RuntimeInformation.OSDescription.Trim();

    /// <summary>Runtime and process architecture, e.g. ".NET 10.0.0 (x64, x64 process)".</summary>
    public static string Runtime() =>
        $"{RuntimeInformation.FrameworkDescription} " +
        $"({RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}, " +
        $"{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()} process)";

    /// <summary>The UI toolkit version, which is the usual suspect when rendering misbehaves.</summary>
    public static string UiToolkit()
    {
        var version = typeof(Avalonia.Application).Assembly.GetName().Version;
        return version is null ? "Avalonia" : $"Avalonia {version.ToString(3)}";
    }

    /// <summary>
    /// Current process memory, e.g. "148 MB working set (52 MB managed)". Working set is the
    /// number Task Manager shows; the managed split says whether growth is ours or the GC's.
    /// </summary>
    public static string MemoryUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / (1024 * 1024);
            var managed = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
            return $"{workingSet.ToString("N0", CultureInfo.CurrentCulture)} MB working set " +
                $"({managed.ToString("N0", CultureInfo.CurrentCulture)} MB managed)";
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            return "unavailable";
        }
    }

    /// <summary>The culture that decides date and money formatting throughout the app.</summary>
    public static string Culture() => CultureInfo.CurrentCulture.Name is { Length: > 0 } name
        ? $"{CultureInfo.CurrentCulture.DisplayName} ({name})"
        : CultureInfo.CurrentCulture.DisplayName;
}
