namespace MoneyCalendar.Core;

/// <summary>
/// Brand constants shared across the app: the product name, the one-line description, and the
/// public URLs the About section links to.
/// </summary>
public static class Brand
{
    /// <summary>The product display name.</summary>
    public const string AppName = "Money Calendar";

    /// <summary>One line, for window titles and the About header.</summary>
    public const string Tagline = "Track money in and money out on a calendar.";

    /// <summary>The paragraph shown in the About section.</summary>
    public const string Description =
        "Money Calendar is a local-first desktop app for hand-tracking income and expenses. " +
        "A range chart shows what came in, what went out, your budget line and your running " +
        "balance; a month calendar puts every day's totals in front of you; and the Income, " +
        "Expenses and Accounts sections keep the detail. Nothing leaves your machine — there " +
        "is no bank connection, no account to create, and no telemetry.";

    /// <summary>The Money Calendar GitHub repository.</summary>
    public const string RepoUrl = "https://github.com/LoxSmoke/money-calendar";

    /// <summary>The README, used as the in-app "Help" link.</summary>
    public const string HelpUrl = "https://github.com/LoxSmoke/money-calendar#readme";

    /// <summary>Where to report a problem.</summary>
    public const string IssuesUrl = "https://github.com/LoxSmoke/money-calendar/issues";

    public const string LicenseName = "MIT License";

    /// <summary>The full license, mirrored by the LICENSE file at the repository root.</summary>
    public const string LicenseText =
        """
        MIT License

        Copyright (c) 2026 LoxSmoke

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;
}
