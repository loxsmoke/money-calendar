using Avalonia.Controls;
using Avalonia.Interactivity;
using MoneyCalendar.App.ViewModels;

namespace MoneyCalendar.App.Views;

/// <summary>
/// Confirms deleting an account, and — when its transactions have to go somewhere — asks where.
/// Closes with the account they should move to, or null when the user backed out.
/// </summary>
public partial class AccountDeleteDialog : Window
{
    public AccountDeleteDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Pre-selects a destination as soon as the plan arrives, so the dialog is never showing an
    /// empty picker with a Delete button that refuses to do anything.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is AccountDeletionPlan { NeedsReassignment: true } && ReplacementPicker.SelectedItem is null)
            ReplacementPicker.SelectedIndex = 0;
    }

    /// <summary>Returns (confirmed, replacement account id).</summary>
    public static async Task<(bool Confirmed, Guid? MoveTo)> ShowAsync(Window owner, AccountDeletionPlan plan)
    {
        var dialog = new AccountDeleteDialog { DataContext = plan };
        var moveTo = await dialog.ShowDialog<Guid?>(owner);
        return (dialog.Confirmed, moveTo);
    }

    private bool Confirmed { get; set; }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountDeletionPlan plan)
            return;

        // A reassignment target is pre-selected, so this only bites if the list emptied out.
        if (plan.NeedsReassignment && ReplacementPicker.SelectedItem is not AccountChoice)
            return;

        Confirmed = true;
        Close(plan.NeedsReassignment ? ((AccountChoice)ReplacementPicker.SelectedItem!).Id : null);
    }
}
