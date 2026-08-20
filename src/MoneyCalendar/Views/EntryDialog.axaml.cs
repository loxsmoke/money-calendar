using Avalonia.Controls;
using Avalonia.Interactivity;
using MoneyCalendar.ViewModels;

namespace MoneyCalendar.Views;

/// <summary>Add/edit dialog. Closes with true only when the editor validated.</summary>
public partial class EntryDialog : Window
{
    public EntryDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // Validation lives in the editor; a failed build leaves the dialog open with the
        // message shown inline.
        if (DataContext is EntryEditorViewModel editor && editor.TryBuild() is not null)
            Close(true);
    }
}
