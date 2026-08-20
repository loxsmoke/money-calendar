using Avalonia.Controls;
using Avalonia.Interactivity;
using MoneyCalendar.App.ViewModels;

namespace MoneyCalendar.App.Views;

public partial class AccountsView : UserControl
{
    public AccountsView()
    {
        InitializeComponent();
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e) => await EditAsync(null);

    private async void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AccountRowViewModel row })
            await EditAsync(row);
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountsViewModel vm
            || sender is not Button { CommandParameter: AccountRowViewModel row }
            || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        // Deleting an account is never silent about its transactions: the plan says how many
        // use it and where they can go.
        var plan = await vm.PrepareDeleteAsync(row);
        var (confirmed, moveTo) = await AccountDeleteDialog.ShowAsync(owner, plan);
        if (confirmed)
            await vm.DeleteAsync(plan, moveTo);
    }

    private async Task EditAsync(AccountRowViewModel? row)
    {
        if (DataContext is not AccountsViewModel vm || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var editor = vm.CreateEditor(row);
        var dialog = new AccountDialog { DataContext = editor };
        if (await dialog.ShowDialog<bool>(owner))
            await vm.SaveEditorAsync(editor);
    }
}
