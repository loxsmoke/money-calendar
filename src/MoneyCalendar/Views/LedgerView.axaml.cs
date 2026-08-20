using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MoneyCalendar.ViewModels;

namespace MoneyCalendar.Views;

/// <summary>
/// Shared code-behind for Income and Expenses. The view owns the dialogs (they need a window
/// owner); the view model owns the data those dialogs edit.
/// </summary>
public partial class LedgerView : UserControl
{
    public LedgerView()
    {
        InitializeComponent();
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e) => await EditAsync(null);

    private async void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: EntryRowViewModel row })
            await EditAsync(row);
    }

    private async void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Sorting means quick repeated clicks on a header, and those bubble up here as a
        // double tap. Only a double click on an actual row opens the editor.
        if (e.Source is not Visual source || source.FindAncestorOfType<DataGridRow>(includeSelf: true) is null)
            return;

        if (DataContext is LedgerViewModel { SelectedRow: { } row })
            await EditAsync(row);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LedgerViewModel vm
            || sender is not Button { CommandParameter: EntryRowViewModel row }
            || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var message = row.IsRecurring
            ? $"Delete the repeating {row.AmountText} ({row.CategoryName}) entry? " +
              $"Every occurrence of \"{row.RepeatText}\" goes with it. This cannot be undone."
            : $"Delete {row.AmountText} ({row.CategoryName}) from {row.DateText}? This cannot be undone.";
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            row.IsRecurring ? "Delete repeating entry" : "Delete entry",
            message,
            "Delete");
        if (confirmed)
            await vm.DeleteAsync(row);
    }

    private async Task EditAsync(EntryRowViewModel? row)
    {
        if (DataContext is not LedgerViewModel vm || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        // Without both ends of the flow the entry could never be saved, so say that up front
        // rather than opening an editor that leads nowhere.
        if (vm.RequiresAccounts)
        {
            if (await ConfirmDialog.ShowAsync(
                    owner, vm.MissingAccountTitle, vm.MissingAccountMessage, "Open Accounts"))
            {
                vm.OpenAccounts();
            }

            return;
        }

        var editor = await vm.CreateEditorAsync(row);
        var dialog = new EntryDialog { DataContext = editor };
        if (await dialog.ShowDialog<bool>(owner))
            await vm.SaveEditorAsync(editor);
    }
}
