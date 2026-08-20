using MoneyCalendar.App.Services;

namespace MoneyCalendar.Tests.Services;

/// <summary>
/// Deciding whether a release is newer than the running build. The network half is not tested
/// here — these are the rules it depends on, and they have to hold without one.
/// </summary>
public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("  v1.2.3  ", "1.2.3")]
    // A pre-release or build suffix names the same version for comparison.
    [InlineData("v1.2.3-rc1", "1.2.3")]
    [InlineData("v1.2.3+abc1234", "1.2.3")]
    // A two-part tag fills the missing part rather than being rejected.
    [InlineData("v2.0", "2.0.0")]
    public void A_tag_reads_as_a_three_part_version(string tag, string expected)
    {
        Assert.Equal(expected, UpdateCheck.ParseTag(tag)?.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("release-2026")]
    public void Anything_that_is_not_a_version_is_not_one(string? tag)
    {
        Assert.Null(UpdateCheck.ParseTag(tag));
        Assert.False(UpdateCheck.IsNewer(new Version(1, 0, 0), tag));
    }

    [Theory]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.1.0", "v1.0.0", true)]
    [InlineData("0.1.0", "v0.1.1", true)]
    [InlineData("0.1.0", "v0.1.0", false)]
    [InlineData("0.2.0", "v0.1.0", false)]
    [InlineData("1.0.0", "v0.9.9", false)]
    public void Newer_means_a_higher_version_not_a_different_one(string current, string tag, bool expected)
    {
        Assert.Equal(expected, UpdateCheck.IsNewer(Version.Parse(current), tag));
    }

    [Fact]
    public void The_assembly_revision_does_not_make_the_running_build_look_ahead()
    {
        // Assembly versions carry a fourth part that releases never name, so 0.1.0.0 has to
        // compare equal to tag v0.1.0 rather than above it.
        Assert.False(UpdateCheck.IsNewer(new Version(0, 1, 0, 0), "v0.1.0"));
        Assert.True(UpdateCheck.IsNewer(new Version(0, 1, 0, 0), "v0.1.1"));
    }

    [Fact]
    public void A_prerelease_of_the_version_you_have_is_not_an_update()
    {
        Assert.False(UpdateCheck.IsNewer(new Version(1, 2, 0), "v1.2.0-rc2"));
    }

    [Fact]
    public void The_asset_name_is_the_one_the_release_workflow_attaches()
    {
        // Kept in step with the Compress-Archive step in .github/workflows/release.yml; if one
        // moves without the other, the Download button silently disappears.
        Assert.Equal("MoneyCalendar-1.2.0-win-x64-portable.zip", UpdateCheck.PortableAssetName("1.2.0"));
    }

    [Fact]
    public void The_running_build_parses_as_a_version_the_check_can_use()
    {
        Assert.True(Version.TryParse(SystemInfo.NumericVersion(), out var version));
        Assert.False(UpdateCheck.IsNewer(version!, "v0.0.1"));
    }
}
