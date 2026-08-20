using Avalonia.Controls;
using Avalonia.Interactivity;
using MoneyCalendar.App.ViewModels;

namespace MoneyCalendar.App.Views;

/// <summary>Typed confirmation for wiping the ledger. Closes with true only when confirmed.</summary>
public partial class DeleteDataDialog : Window
{
    public DeleteDataDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, DeleteDataConfirmViewModel confirm)
    {
        var dialog = new DeleteDataDialog { DataContext = confirm };
        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        // The button is bound to CanDelete, but a keyboard default could still reach it.
        if (DataContext is DeleteDataConfirmViewModel { CanDelete: true })
            Close(true);
    }
}
