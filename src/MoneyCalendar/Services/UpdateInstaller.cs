using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MoneyCalendar.Services;

/// <summary>
/// Downloads a release's setup.exe and, for the Install action, runs it.
/// </summary>
/// <remarks>
/// GitHub Releases publish no SHA-256 and the release workflow does not Authenticode-sign, so
/// integrity rests on two checks rather than a hash: the URL must be https on a GitHub-owned
/// host, and the downloaded length must match the size the API reported.
/// </remarks>
public static class UpdateInstaller
{
    /// <summary>The stable Inno <c>AppId</c> from build/MoneyCalendar.iss; <c>_is1</c> is Inno's suffix.</summary>
    private const string UninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DA9F03E4-784F-4B21-95DF-4BCA2B517FF7}_is1";

    /// <summary>Subdirectory of the local app-data folder holding a downloaded installer.</summary>
    private const string UpdateDirName = "update";

    /// <summary>Hosts a release asset may be served from. Suffix-matched on a label boundary.</summary>
    private static readonly string[] AllowedHosts = ["github.com", "githubusercontent.com"];

    /// <summary>
    /// <c>FOLDERID_Downloads</c> from the Windows SDK's <c>KnownFolders.h</c>. Downloads
    /// post-dates the CSIDL scheme that <see cref="Environment.SpecialFolder"/> wraps, so
    /// there is no managed constant for it and the GUID has to be spelled out here.
    /// </summary>
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>
    /// True when this build is the one setup installed, so replacing it in place is the right
    /// thing to do. A portable copy — or a second copy beside the installed one — is left alone:
    /// running setup would update the *other* install and leave this one untouched.
    /// </summary>
    public static bool IsInstalledBySetup()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation") as string is not { Length: > 0 } location)
                return false;

            return SamePath(location, AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static string DownloadsFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero, out var path) == 0
                    && !string.IsNullOrEmpty(path))
                    return path;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Fall through to the default location.
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>Where an installer waits between download and launch. Not the ledger's folder.</summary>
    public static string UpdateStagingDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoneyCalendar",
            UpdateDirName);

    /// <summary>
    /// Fetches the release's installer into <paramref name="destDir"/> and returns its path, or
    /// null if anything at all went wrong. Retries twice on a dropped connection.
    /// </summary>
    public static async Task<string?> DownloadAsync(
        ReleaseInfo release,
        string destDir,
        IProgress<double>? progress = null,
        HttpClient? http = null,
        CancellationToken ct = default)
    {
        if (release.SetupUrl is not { } url || !IsTrustedUrl(url))
            return null;

        var client = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        try
        {
            Directory.CreateDirectory(destDir);
            var destination = UniquePath(destDir, UpdateCheck.SetupAssetName(release.Version));
            var partial = destination + ".part";
            TryDelete(partial);

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await FetchToFileAsync(client, url, partial, release.SetupSize, progress, ct)
                        .ConfigureAwait(false);
                    break;
                }
                catch (Exception ex)
                    when (ex is HttpRequestException or IOException && attempt < 2 && !ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << attempt)), ct).ConfigureAwait(false);
                }
            }

            File.Move(partial, destination, overwrite: true);
            return destination;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Downloading the update failed");
            return null;
        }
        finally
        {
            if (http is null)
                client.Dispose();
        }
    }

    private static async Task FetchToFileAsync(
        HttpClient client,
        string url,
        string partial,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = expectedSize > 0 ? expectedSize : response.Content.Headers.ContentLength ?? 0;
        var written = 0L;

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    written += read;
                    if (total > 0)
                        progress?.Report(Math.Min(1.0, (double)written / total));
                }
            }

            // No hash to check against, so a length that disagrees with the API is the only
            // signal that what arrived is not what was published.
            if (expectedSize > 0 && written != expectedSize)
                throw new IOException($"size mismatch: expected {expectedSize} bytes, got {written}");
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>
    /// Runs the downloaded installer over this install and starts the app again afterwards.
    /// <c>/RELAUNCH</c> is our own switch: <c>/SILENT</c> suppresses Inno's post-install launch,
    /// so without it the app would update and never come back.
    /// </summary>
    public static void Launch(string setupPath)
    {
        var startInfo = new ProcessStartInfo(setupPath) { UseShellExecute = true };
        startInfo.ArgumentList.Add("/SILENT");
        startInfo.ArgumentList.Add("/NORESTART");
        startInfo.ArgumentList.Add("/RELAUNCH");
        Process.Start(startInfo);
    }

    /// <summary>Opens Explorer with the downloaded file selected.</summary>
    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Revealing {Path} failed", path);
        }
    }

    /// <summary>https on a GitHub-owned host, matched on a label boundary so "notgithub.com" fails.</summary>
    public static bool IsTrustedUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        foreach (var allowed in AllowedHosts)
        {
            if (uri.Host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>"setup.exe", "setup (2).exe", … so a second download never clobbers the first.</summary>
    public static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, fileName);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort only.
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint flags,
        IntPtr token,
        [MarshalAs(UnmanagedType.LPWStr)] out string path);
}
