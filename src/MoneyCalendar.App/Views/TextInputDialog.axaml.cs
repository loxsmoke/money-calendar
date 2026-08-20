using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MoneyCalendar.App.Views;

/// <summary>
/// Asks for one line of text. Returns null when cancelled, so callers can tell "nothing typed"
/// apart from "dismissed". Validation belongs to the caller — this only refuses an empty box.
/// </summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog()
    {
        InitializeComponent();
    }

    public static async Task<string?> ShowAsync(
        Window owner, string title, string prompt, string acceptText, string initialValue = "")
    {
        var dialog = new TextInputDialog { Title = title };
        dialog.PromptText.Text = prompt;
        dialog.AcceptButton.Content = acceptText;
        dialog.ValueBox.Text = initialValue;
        dialog.Opened += (_, _) =>
        {
            dialog.ValueBox.SelectAll();
            dialog.ValueBox.Focus();
        };
        return await dialog.ShowDialog<string?>(owner);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnAcceptClick(object? sender, RoutedEventArgs e) => Accept();

    private void OnValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        Accept();
        e.Handled = true;
    }

    private void Accept()
    {
        var value = ValueBox.Text?.Trim();
        if (!string.IsNullOrEmpty(value))
            Close(value);
    }
}
