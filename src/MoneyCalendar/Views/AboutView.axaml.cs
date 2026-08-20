using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MoneyCalendar.ViewModels;

namespace MoneyCalendar.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    private void OnGitHub(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel vm)
            OpenUrl(vm.RepoUrl);
    }

    private void OnIssues(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel vm)
            OpenUrl(vm.IssuesUrl);
    }

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel vm)
            await vm.CheckForUpdatesAsync(announceWhenCurrent: true);
    }

    /// <summary>
    /// Replaces this install with the new one. Setup cannot overwrite a running exe, so the app
    /// closes as soon as setup is launched; its /RELAUNCH switch starts the new build after.
    /// </summary>
    private async void OnInstallUpdate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AboutViewModel vm || !await vm.InstallUpdateAsync())
            return;

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>Saves the installer to the Downloads folder and shows it in Explorer.</summary>
    private async void OnDownloadUpdate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel vm)
            await vm.DownloadUpdateAsync();
    }

    private void OnViewRelease(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel vm)
            OpenUrl(vm.ReleaseUrl);
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AboutViewModel vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        try
        {
            await clipboard.SetTextAsync(vm.BuildDiagnostics());
            if (sender is Button button)
            {
                button.Content = "Copied ✓";
                await Task.Delay(1500);
                button.Content = "Copy info";
            }
        }
        catch (Exception ex)
        {
            // Clipboard access can fail when another app holds it — nothing to signal.
            Serilog.Log.Warning(ex, "Copying system info failed");
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // No browser, or the shell refused the handler — the URL is on screen either way.
            Serilog.Log.Warning(ex, "Opening {Url} failed", url);
        }
    }
}
