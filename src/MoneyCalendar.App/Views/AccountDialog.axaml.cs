using Avalonia.Controls;
using Avalonia.Interactivity;
using MoneyCalendar.App.ViewModels;

namespace MoneyCalendar.App.Views;

/// <summary>Add/edit dialog for one account. Closes with true only when the editor validated.</summary>
public partial class AccountDialog : Window
{
    public AccountDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountEditorViewModel editor && editor.TryBuild() is not null)
            Close(true);
    }
}
