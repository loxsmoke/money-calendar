namespace MoneyCalendar.Services;

/// <summary>What the latest GitHub release says about itself.</summary>
public sealed record ReleaseInfo(string Version, DateTimeOffset PublishedAt, string? SetupUrl, long SetupSize);

/// <summary>
/// Comparing the running build against a release tag. Pure and side-effect free, so the rules
/// that decide "there is a newer version" are testable without a network.
/// </summary>
public static class UpdateCheck
{
    /// <summary>How long a check holds before the app looks again.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// The installer the release workflow attaches. This is the asset the app looks for: the
    /// portable zip beside it is for people who found it on the releases page themselves.
    /// </summary>
    public static string SetupAssetName(string version) => $"MoneyCalendar-{version}-setup.exe";

    /// <summary>The portable zip, named here so a test can pin what the workflow produces.</summary>
    public static string PortableAssetName(string version) =>
        $"MoneyCalendar-{version}-win-x64-portable.zip";

    /// <summary>
    /// A release tag as a version. Accepts a leading "v", and drops any pre-release or build
    /// suffix — "v1.2.0-rc1" is version 1.2.0 for comparison. Null when it is not a version.
    /// </summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var text = tag.Trim();
        if (text[0] is 'v' or 'V')
            text = text[1..];

        var cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
            text = text[..cut];

        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    /// <summary>True when the tag names a version above the one running.</summary>
    public static bool IsNewer(Version current, string? latestTag) =>
        ParseTag(latestTag) is { } latest && latest > Normalize(current);

    /// <summary>
    /// Three parts, always. Assembly versions carry a fourth (revision) that releases never
    /// name, so comparing without flattening it would make 1.2.0.0 look newer than tag v1.2.0.
    /// </summary>
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);
}
