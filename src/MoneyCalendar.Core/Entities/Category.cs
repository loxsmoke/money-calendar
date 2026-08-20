namespace MoneyCalendar.Core.Entities;

/// <summary>
/// An income type (Salary, Investment, …) or an expense category (Rent, Utilities, …).
/// Built-in categories are seeded with deterministic ids and cannot be deleted; users add
/// their own from the Income/Expenses sections.
/// </summary>
public class Category
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public EntryKind Kind { get; set; }

    /// <summary>Pill/legend color, "#RRGGBB".</summary>
    public required string ColorHex { get; set; }

    /// <summary>Seeded category: renameable, never deletable.</summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Categories that name a funding instrument (credit card, mortgage) prompt for an
    /// account label and its last digits when an entry is created.
    /// </summary>
    public bool WantsAccountDetails { get; set; }

    public int SortOrder { get; set; }
}
