using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MoneyCalendar.Core;

namespace MoneyCalendar.Services;

/// <summary>
/// Asks GitHub for the latest release and reports it only when it is newer than the running
/// build. The one outbound request the app ever makes, and it is opt-out: nothing about the
/// machine or the ledger goes with it, and every failure is silent — an app that cannot reach
/// GitHub is not a broken app.
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient Default = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<ReleaseInfo?> CheckAsync(
        Version current, HttpClient? http = null, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Brand.LatestReleaseApiUrl);
            request.Headers.UserAgent.ParseAdd("MoneyCalendar-update-check");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await (http ?? Default).SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElement))
                return null;

            var tag = tagElement.GetString();
            if (!UpdateCheck.IsNewer(current, tag))
                return null;

            var version = UpdateCheck.ParseTag(tag)!.ToString();
            var (url, size) = FindSetupAsset(root, version);
            return new ReleaseInfo(version, PublishedAt(root), url, size);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Serilog.Log.Debug(ex, "Update check did not complete");
            return null;
        }
    }

    private static DateTimeOffset PublishedAt(JsonElement root) =>
        root.TryGetProperty("published_at", out var element)
        && element.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    /// <summary>
    /// The installer for that version, when the release carries one. A release without it
    /// leaves the Install and Download actions hidden — there is nothing for them to fetch.
    /// </summary>
    private static (string? Url, long Size) FindSetupAsset(JsonElement root, string version)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, 0);

        var wanted = UpdateCheck.SetupAssetName(version);
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name)
                || !string.Equals(name.GetString(), wanted, StringComparison.OrdinalIgnoreCase)
                || !asset.TryGetProperty("browser_download_url", out var url)
                || url.GetString() is not { Length: > 0 } link)
                continue;

            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes)
                ? bytes
                : 0;
            return (link, size);
        }

        return (null, 0);
    }
}
