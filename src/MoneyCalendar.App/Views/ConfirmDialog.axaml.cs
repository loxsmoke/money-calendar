using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoneyCalendar.App.Views;

/// <summary>Small yes/no dialog for destructive actions (delete an entry, wipe the data).</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
