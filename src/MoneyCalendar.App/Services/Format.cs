using System.Globalization;
using MoneyCalendar.Core.Abstractions;

namespace MoneyCalendar.App.Services;

/// <summary>Display formatting shared by all view models.</summary>
public static class Format
{
    /// <summary>"$1,234.50". <paramref name="explicitSign"/> prefixes '+' on positive amounts.</summary>
    public static string Money(decimal amount, string currencyCode, bool explicitSign = false)
    {
        var symbol = Symbol(currencyCode);
        var magnitude = Math.Abs(amount).ToString("N2", CultureInfo.CurrentCulture);
        var formatted = symbol is not null ? $"{symbol}{magnitude}" : $"{magnitude} {currencyCode}";
        if (amount < 0)
            return $"-{formatted}";
        return explicitSign && amount > 0 ? $"+{formatted}" : formatted;
    }

    /// <summary>Compact form for calendar pills: "$1.2k" once the amount stops fitting.</summary>
    public static string CompactMoney(decimal amount, string currencyCode)
    {
        var symbol = Symbol(currencyCode) ?? "";
        var magnitude = Math.Abs(amount);
        if (magnitude >= 10_000m)
            return $"{symbol}{(magnitude / 1000m).ToString("0.#", CultureInfo.CurrentCulture)}k";
        if (magnitude >= 1_000m)
            return $"{symbol}{(magnitude / 1000m).ToString("0.0", CultureInfo.CurrentCulture)}k";
        return $"{symbol}{magnitude.ToString("0.##", CultureInfo.CurrentCulture)}";
    }

    public static string? Symbol(string currencyCode) => currencyCode switch
    {
        "USD" => "$",
        "EUR" => "€",
        "GBP" => "£",
        "CAD" => "CA$",
        _ => null,
    };

    public static string MonthName(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    public static string ShortDate(DateOnly date) =>
        date.ToString("MMM d", CultureInfo.CurrentCulture);

    public static string LongDate(DateOnly date) =>
        date.ToString("ddd, MMM d, yyyy", CultureInfo.CurrentCulture);

    /// <summary>List rows: the date without its weekday, which only costs width.</summary>
    public static string MediumDate(DateOnly date) =>
        date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

    public static string RangeText(DateRange range) =>
        $"{range.From.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} – " +
        $"{range.To.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";

    public static string Percent(double share) => share.ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>"48 KB", "1.4 MB" — enough to tell an empty database from a full one.</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes.ToString("N0", CultureInfo.CurrentCulture)} B";
        if (bytes < 1024 * 1024)
            return $"{(bytes / 1024d).ToString("N0", CultureInfo.CurrentCulture)} KB";
        return $"{(bytes / (1024d * 1024d)).ToString("N1", CultureInfo.CurrentCulture)} MB";
    }

    public static string Count(int value, string singular, string plural) =>
        $"{value.ToString("N0", CultureInfo.CurrentCulture)} {(value == 1 ? singular : plural)}";

    /// <summary>Card/account suffix: "Sapphire Visa ••••4417".</summary>
    public static string? Account(string? name, string? last4)
    {
        var hasName = !string.IsNullOrWhiteSpace(name);
        var hasLast4 = !string.IsNullOrWhiteSpace(last4);
        return (hasName, hasLast4) switch
        {
            (true, true) => $"{name} ••••{last4}",
            (true, false) => name,
            (false, true) => $"••••{last4}",
            _ => null,
        };
    }
}
