using Avalonia.Controls;
using Avalonia.Interactivity;
using MoneyCalendar.App.ViewModels;

namespace MoneyCalendar.App.Views;

/// <summary>Add/edit dialog for one category. Closes with true only when the editor validated.</summary>
public partial class CategoryDialog : Window
{
    public CategoryDialog()
    {
        InitializeComponent();
    }

    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CategoryEditorViewModel editor
            && sender is Button { CommandParameter: ColorChoice color })
        {
            editor.PickColor(color);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CategoryEditorViewModel editor && editor.TryBuild() is not null)
            Close(true);
    }
}
