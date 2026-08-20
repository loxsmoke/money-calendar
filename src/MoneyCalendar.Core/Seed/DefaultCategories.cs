using MoneyCalendar.Core.Entities;

namespace MoneyCalendar.Core.Seed;

/// <summary>
/// Built-in income types and expense categories. Ids are deterministic so seeding is
/// idempotent and exported files import cleanly into a fresh database.
/// </summary>
public static class DefaultCategories
{
    private static Guid Id(string suffix) => Guid.Parse("0000000a-0000-4000-8000-0000000000" + suffix);

    // Income types (spec: salary, investment, interest, tips, other).
    public static Guid Salary { get; } = Id("11");
    public static Guid Investment { get; } = Id("12");
    public static Guid Interest { get; } = Id("13");
    public static Guid Tips { get; } = Id("14");
    public static Guid OtherIncome { get; } = Id("15");

    // Expense categories.
    public static Guid Rent { get; } = Id("21");
    public static Guid Utilities { get; } = Id("22");
    public static Guid CreditCard { get; } = Id("23");
    public static Guid Mortgage { get; } = Id("24");
    public static Guid Fee { get; } = Id("25");
    public static Guid Groceries { get; } = Id("26");
    public static Guid Transport { get; } = Id("27");
    public static Guid Subscription { get; } = Id("29");
    public static Guid OtherExpense { get; } = Id("28");

    public static IReadOnlyList<Category> All { get; } =
    [
        new() { Id = Salary, Name = "Salary", Kind = EntryKind.Income, ColorHex = "#1B7F3B", IsSystem = true, SortOrder = 10 },
        new() { Id = Investment, Name = "Investment", Kind = EntryKind.Income, ColorHex = "#2E7D6F", IsSystem = true, SortOrder = 20 },
        new() { Id = Interest, Name = "Interest", Kind = EntryKind.Income, ColorHex = "#4C9A2A", IsSystem = true, SortOrder = 30 },
        new() { Id = Tips, Name = "Tips", Kind = EntryKind.Income, ColorHex = "#7CB342", IsSystem = true, SortOrder = 40 },
        new() { Id = OtherIncome, Name = "Other", Kind = EntryKind.Income, ColorHex = "#8D9F6E", IsSystem = true, SortOrder = 50 },

        new() { Id = Rent, Name = "Rent", Kind = EntryKind.Expense, ColorHex = "#B3541E", IsSystem = true, SortOrder = 10 },
        new() { Id = Utilities, Name = "Utilities", Kind = EntryKind.Expense, ColorHex = "#C78A00", IsSystem = true, SortOrder = 20 },
        new() { Id = CreditCard, Name = "Credit card", Kind = EntryKind.Expense, ColorHex = "#8E44AD", IsSystem = true, WantsAccountDetails = true, SortOrder = 30 },
        new() { Id = Mortgage, Name = "Mortgage", Kind = EntryKind.Expense, ColorHex = "#4A6FA5", IsSystem = true, WantsAccountDetails = true, SortOrder = 40 },
        new() { Id = Fee, Name = "Fee", Kind = EntryKind.Expense, ColorHex = "#A0522D", IsSystem = true, SortOrder = 50 },
        new() { Id = Groceries, Name = "Groceries", Kind = EntryKind.Expense, ColorHex = "#3F7D5C", IsSystem = true, SortOrder = 60 },
        new() { Id = Transport, Name = "Transport", Kind = EntryKind.Expense, ColorHex = "#5C6BC0", IsSystem = true, SortOrder = 70 },
        new() { Id = Subscription, Name = "Subscription", Kind = EntryKind.Expense, ColorHex = "#B4506B", IsSystem = true, SortOrder = 80 },
        new() { Id = OtherExpense, Name = "Other", Kind = EntryKind.Expense, ColorHex = "#8A8A92", IsSystem = true, SortOrder = 999 },
    ];

    /// <summary>Palette offered when a user adds a custom category.</summary>
    public static IReadOnlyList<string> CustomPalette { get; } =
        ["#D4636F", "#E08A3C", "#C9A227", "#4FA3A5", "#6C7BD1", "#9B6BC9", "#5E8C61", "#7A7A85"];
}
