using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.App.ViewModels;

/// <summary>Which ledger a category belongs to, as offered by the editor's picker.</summary>
public sealed record CategoryKindChoice(EntryKind Kind, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One swatch in the color palette.</summary>
public partial class ColorChoice(string hex) : ViewModelBase
{
    public string Hex { get; } = hex;
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(hex));

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Add/edit dialog state for a category: its name, which ledger it belongs to, and its color.
/// A category's kind is fixed once it exists — moving it would strand the entries filed under
/// it in the other section.
/// </summary>
public partial class CategoryEditorViewModel : ViewModelBase
{
    private readonly Category? _original;
    private readonly IReadOnlyList<Category> _existing;

    public CategoryEditorViewModel(Category? existing, IReadOnlyList<Category> allCategories, EntryKind defaultKind)
    {
        _original = existing;
        _existing = allCategories;

        Title = existing is null ? "Add category" : "Edit category";
        Kinds = [new(EntryKind.Income, "Income"), new(EntryKind.Expense, "Expense")];
        Colors = BuildPalette(existing?.ColorHex);

        _name = existing?.Name ?? "";
        _selectedKind = Kinds.First(k => k.Kind == (existing?.Kind ?? defaultKind));
        _selectedColor = Colors.FirstOrDefault(c => c.IsSelected) ?? Colors[0];
        _selectedColor.IsSelected = true;
    }

    public string Title { get; }
    public IReadOnlyList<CategoryKindChoice> Kinds { get; }
    public IReadOnlyList<ColorChoice> Colors { get; }
    public bool IsNew => _original is null;

    /// <summary>Built-in categories keep their side of the app; only new ones choose.</summary>
    public bool CanChangeKind => IsNew;

    [ObservableProperty] private string _name;
    [ObservableProperty] private CategoryKindChoice _selectedKind;
    [ObservableProperty] private ColorChoice _selectedColor;
    [ObservableProperty] private string? _errorText;

    /// <summary>Palette clicks come through here so only one swatch stays lit.</summary>
    public void PickColor(ColorChoice color)
    {
        foreach (var swatch in Colors)
            swatch.IsSelected = ReferenceEquals(swatch, color);
        SelectedColor = color;
    }

    /// <summary>Validates and produces the entity to persist, or null with <see cref="ErrorText"/> set.</summary>
    public Category? TryBuild()
    {
        var name = Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            ErrorText = "Give the category a name.";
            return null;
        }

        var kind = SelectedKind.Kind;
        if (_existing.Any(c => c.Id != _original?.Id
            && c.Kind == kind
            && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = $"'{name}' already exists in {SelectedKind.Label.ToLowerInvariant()}.";
            return null;
        }

        ErrorText = null;
        return new Category
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = name,
            Kind = kind,
            ColorHex = SelectedColor.Hex,
            IsSystem = _original?.IsSystem ?? false,
            WantsAccountDetails = _original?.WantsAccountDetails ?? false,
            SortOrder = _original?.SortOrder ?? 500,
        };
    }

    /// <summary>The built-in category colors plus the custom palette, without duplicates.</summary>
    private static IReadOnlyList<ColorChoice> BuildPalette(string? selected)
    {
        var hexes = DefaultCategories.All.Select(c => c.ColorHex)
            .Concat(DefaultCategories.CustomPalette)
            .Concat(selected is null ? [] : new[] { selected })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var swatches = hexes.Select(hex => new ColorChoice(hex)).ToList();
        var match = swatches.FirstOrDefault(c => string.Equals(c.Hex, selected, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            match.IsSelected = true;
        return swatches;
    }
}
