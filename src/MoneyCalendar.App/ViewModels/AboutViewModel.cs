using System.Text;
using MoneyCalendar.App.Bootstrap;
using MoneyCalendar.App.Services;
using MoneyCalendar.Core;
using MoneyCalendar.Core.Abstractions;

namespace MoneyCalendar.App.ViewModels;

/// <summary>One labelled fact in the system-info table.</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>
/// About section: what the app is, which build is running, where to find the source, the
/// machine facts worth quoting in a bug report, and the license.
/// </summary>
public sealed class AboutViewModel(IEntryRepository entries) : PageViewModel
{
    public override string Title => "About";

    public string AppName => Brand.AppName;
    public string Tagline => Brand.Tagline;
    public string Description => Brand.Description;
    public string RepoUrl => Brand.RepoUrl;
    public string IssuesUrl => Brand.IssuesUrl;
    public string LicenseName => Brand.LicenseName;
    public string LicenseText => Brand.LicenseText;

    public string VersionText { get; } = $"Version {Services.SystemInfo.AppVersion()}";
    public string LicenseSummary { get; } =
        $"{Brand.AppName} is free and open source under the {Brand.LicenseName}.";

    private IReadOnlyList<InfoRow> _systemInfo = [];
    public IReadOnlyList<InfoRow> SystemInfo
    {
        get => _systemInfo;
        private set => SetProperty(ref _systemInfo, value);
    }

    protected override async Task<bool> LoadAsync(CancellationToken ct)
    {
        // Memory and the entry count move between visits, so this is rebuilt on every load.
        var count = await entries.CountAsync(ct);
        SystemInfo =
        [
            new("Version", Services.SystemInfo.AppVersion()),
            new("Operating system", Services.SystemInfo.OperatingSystem()),
            new("Runtime", Services.SystemInfo.Runtime()),
            new("UI toolkit", Services.SystemInfo.UiToolkit()),
            new("Memory", Services.SystemInfo.MemoryUsage()),
            new("Regional format", Services.SystemInfo.Culture()),
            new("Stored entries", Format.Count(count, "entry", "entries")),
            new("Database file", AppDataPaths.DatabaseFile),
            new("Log folder", AppDataPaths.Logs),
        ];
        return true;
    }

    /// <summary>The system-info table as plain text, for the "Copy info" button.</summary>
    public string BuildDiagnostics()
    {
        var text = new StringBuilder();
        text.Append(Brand.AppName).Append(' ').AppendLine(VersionText);
        foreach (var row in SystemInfo.Where(r => r.Label != "Version"))
            text.Append(row.Label).Append(": ").AppendLine(row.Value);
        return text.ToString().TrimEnd();
    }
}
