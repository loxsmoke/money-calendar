using MoneyCalendar.Services;
using MoneyCalendar.ViewModels;

namespace MoneyCalendar.Tests.Ui;

/// <summary>
/// The title bar carries the running build, so a bug report can name it without opening About.
/// It shows the numeric version only — a prerelease suffix or a build sha says nothing there.
/// </summary>
public class WindowTitleTests
{
    [Fact]
    public void The_title_is_the_product_name_and_the_version()
    {
        Assert.Equal("Money Calendar - 0.1.0", MainWindowViewModel.BuildWindowTitle("0.1.0"));
    }

    [Theory]
    [InlineData(0, 1, 0, 0, "0.1.0")]
    [InlineData(1, 2, 3, 4, "1.2.3")]
    [InlineData(2, 0, 0, 0, "2.0.0")]
    public void The_version_is_three_numeric_parts(int major, int minor, int build, int revision, string expected)
    {
        Assert.Equal(expected, SystemInfo.FormatNumericVersion(new Version(major, minor, build, revision)));
    }

    [Fact]
    public void A_missing_assembly_version_falls_back_rather_than_blanking_the_title()
    {
        Assert.Equal("0.1.0", SystemInfo.FormatNumericVersion(null));
    }

    [Fact]
    public void The_running_build_produces_a_numeric_title()
    {
        var version = SystemInfo.NumericVersion();

        Assert.Equal(3, version.Split('.').Length);
        Assert.All(version.Split('.'), part => Assert.True(int.TryParse(part, out _), part));
        Assert.Equal($"Money Calendar - {version}", MainWindowViewModel.BuildWindowTitle(version));
    }
}
