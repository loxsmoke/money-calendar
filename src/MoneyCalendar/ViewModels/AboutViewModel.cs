using System.Text;
using MoneyCalendar.Bootstrap;
using MoneyCalendar.Services;
using MoneyCalendar.Core;
using MoneyCalendar.Core.Abstractions;

namespace MoneyCalendar.ViewModels;

/// <summary>One labelled fact in the system-info table.</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>
/// About section: what the app is, which build is running, where to find the source, the
/// machine facts worth quoting in a bug report, and the license.
/// </summary>
public sealed partial class AboutViewModel(IEntryRepository entries, ISettingsStore settings) : PageViewModel
{
    private ReleaseInfo? _latest;

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

    // ---- updates ----------------------------------------------------------

    /// <summary>Off means the app makes no network requests at all.</summary>
    public bool CheckForUpdates
    {
        get => settings.Current.CheckForUpdates;
        set
        {
            if (value == settings.Current.CheckForUpdates)
                return;

            _ = settings.SaveAsync(settings.Current with { CheckForUpdates = value }, CancellationToken.None);
            OnPropertyChanged();
            UpdateStatusText = value ? null : "Update checks are off.";
        }
    }

    private string? _updateStatusText;
    public string? UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set => SetProperty(ref _updateAvailable, value);
    }

    private bool _updateBusy;
    public bool UpdateBusy
    {
        get => _updateBusy;
        private set => SetProperty(ref _updateBusy, value);
    }

    /// <summary>The page for the newer release, or the releases page when there is none.</summary>
    public string ReleaseUrl => _latest is null ? Brand.ReleasesUrl : Brand.ReleaseTagUrl(_latest.Version);

    /// <summary>There is an installer to fetch, so the download action has something to do.</summary>
    public bool CanDownloadUpdate => _latest?.SetupUrl is not null;

    /// <summary>
    /// True only when this build is the one setup installed, so running the new installer
    /// replaces *this* copy. A portable build offers the download instead: installing would
    /// update some other install and leave this one where it is.
    /// </summary>
    private bool _canInstallUpdate;
    public bool CanInstallUpdate
    {
        get => _canInstallUpdate;
        private set => SetProperty(ref _canInstallUpdate, value);
    }

    private double _updateProgress;
    public double UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, value);
    }

    /// <summary>
    /// Downloads the installer and runs it over this install, silently, asking it to start the
    /// app again afterwards. Returns true when setup is running and the app should close.
    /// </summary>
    public async Task<bool> InstallUpdateAsync()
    {
        if (_latest is not { } release || UpdateBusy || !CanInstallUpdate)
            return false;

        UpdateBusy = true;
        UpdateProgress = 0;
        try
        {
            var path = await UpdateInstaller.DownloadAsync(
                release, UpdateInstaller.UpdateStagingDir(), new Progress<double>(p => UpdateProgress = p));
            if (path is null)
            {
                UpdateStatusText = "The update could not be downloaded. Try the release page instead.";
                CanInstallUpdate = false;
                return false;
            }

            UpdateStatusText = "Installing…";
            UpdateInstaller.Launch(path);
            return true;
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    /// <summary>Saves the installer to the Downloads folder and shows it in Explorer.</summary>
    public async Task DownloadUpdateAsync()
    {
        if (_latest is not { } release || UpdateBusy || !CanDownloadUpdate)
            return;

        UpdateBusy = true;
        UpdateProgress = 0;
        try
        {
            var path = await UpdateInstaller.DownloadAsync(
                release, UpdateInstaller.DownloadsFolder(), new Progress<double>(p => UpdateProgress = p));
            if (path is null)
            {
                UpdateStatusText = "The update could not be downloaded. Try the release page instead.";
                return;
            }

            UpdateStatusText = $"Saved to {path}.";
            UpdateInstaller.Reveal(path);
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    /// <summary>
    /// Asks GitHub whether there is anything newer. Silent about failure by design — a machine
    /// that is offline should not be told its app is broken.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool announceWhenCurrent)
    {
        if (!CheckForUpdates || UpdateBusy)
            return;

        UpdateBusy = true;
        try
        {
            var current = Version.TryParse(Services.SystemInfo.NumericVersion(), out var parsed)
                ? parsed
                : new Version(0, 0, 0);
            _latest = await UpdateService.CheckAsync(current);

            UpdateAvailable = _latest is not null;
            CanInstallUpdate = _latest?.SetupUrl is not null && UpdateInstaller.IsInstalledBySetup();
            UpdateStatusText = _latest is not null
                ? $"Version {_latest.Version} is available."
                : announceWhenCurrent ? "This is the latest version." : null;
            OnPropertyChanged(nameof(ReleaseUrl));
            OnPropertyChanged(nameof(CanDownloadUpdate));
        }
        finally
        {
            UpdateBusy = false;
        }
    }

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

        // Quietly, on arriving at the page: nothing is said unless there is something newer.
        _ = CheckForUpdatesAsync(announceWhenCurrent: false);
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
