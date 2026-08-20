using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MoneyCalendar.ViewModels;

namespace MoneyCalendar.Views;

public partial class SettingsView : UserControl
{
    private static readonly FilePickerFileType JsonType = new("JSON backup") { Patterns = ["*.json"] };
    private static readonly FilePickerFileType CsvType = new("CSV") { Patterns = ["*.csv"] };

    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnAddCategoryClick(object? sender, RoutedEventArgs e) => await EditCategoryAsync(null);

    private async void OnEditCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: CategoryRowViewModel row })
            await EditCategoryAsync(row);
    }

    private async void OnDeleteCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm
            || sender is not Button { CommandParameter: CategoryRowViewModel row }
            || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            "Delete category",
            $"Delete the '{row.Name}' category? Categories with entries filed under them are kept.",
            "Delete");
        if (confirmed)
            await vm.DeleteCategoryAsync(row);
    }

    private async Task EditCategoryAsync(CategoryRowViewModel? row)
    {
        if (DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var editor = await vm.CreateCategoryEditorAsync(row);
        var dialog = new CategoryDialog { DataContext = editor };
        if (await dialog.ShowDialog<bool>(owner))
            await vm.SaveCategoryAsync(editor);
    }

    private async void OnExportJsonClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top))
            return;

        var path = await SaveAsync(top, "Export JSON backup", vm.SuggestedJsonName, JsonType);
        if (path is not null)
            await vm.ExportJsonAsync(path);
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top))
            return;

        var path = await SaveAsync(top, "Export CSV", vm.SuggestedCsvName, CsvType);
        if (path is not null)
            await vm.ExportCsvAsync(path);
    }

    private async void OnImportJsonClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top))
            return;

        var path = await OpenAsync(top, "Import JSON backup", JsonType);
        if (path is not null && await ConfirmImportAsync(vm, path))
            await vm.ImportJsonAsync(path);
    }

    private async void OnImportCsvClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top))
            return;

        var path = await OpenAsync(top, "Import CSV", CsvType);
        if (path is not null && await ConfirmImportAsync(vm, path))
            await vm.ImportCsvAsync(path);
    }

    private async void OnClearTransactionsClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top) || top is not Window owner)
            return;

        // An empty ledger has nothing to lose, so it does not get the typed confirmation.
        if (vm.TransactionCount == 0)
        {
            vm.ReportNothingToDelete();
            return;
        }

        var confirm = new DeleteDataConfirmViewModel(vm.TransactionCount);
        if (await DeleteDataDialog.ShowAsync(owner, confirm))
            await vm.ClearTransactionsCommand.ExecuteAsync(null);
    }

    private async void OnClearDataClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top) || top is not Window owner)
            return;

        if (vm.TransactionCount == 0 && vm.AccountCount == 0)
        {
            vm.ReportNothingToDelete();
            return;
        }

        var confirm = new DeleteDataConfirmViewModel(
            vm.TransactionCount, DeleteScope.Everything, vm.AccountCount);
        if (await DeleteDataDialog.ShowAsync(owner, confirm))
            await vm.ClearAllDataCommand.ExecuteAsync(null);
    }

    // ---- databases (Developer) --------------------------------------------

    private async void OnCreateDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top) || top is not Window owner)
            return;

        var name = await TextInputDialog.ShowAsync(
            owner,
            "New database",
            "A name for the new database. It starts empty, and the app keeps using the current one until you select it.",
            "Create");
        if (name is not null)
            await vm.CreateDatabaseAsync(name);
    }

    private async void OnSelectDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, _) || sender is not Button { CommandParameter: DatabaseRowViewModel row })
            return;

        await vm.SelectDatabaseAsync(row);
    }

    private async void OnCloneDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top) || top is not Window owner
            || sender is not Button { CommandParameter: DatabaseRowViewModel row })
            return;

        var name = await TextInputDialog.ShowAsync(
            owner,
            "Clone database",
            $"A name for the copy of '{row.Name}'. Everything in it is copied as it stands.",
            "Clone",
            row.Name + " copy");
        if (name is not null)
            await vm.CloneDatabaseAsync(row, name);
    }

    private async void OnRenameDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top) || top is not Window owner
            || sender is not Button { CommandParameter: DatabaseRowViewModel row })
            return;

        var name = await TextInputDialog.ShowAsync(
            owner, "Rename database", $"A new name for '{row.Name}'.", "Rename", row.Name);
        if (name is not null && !string.Equals(name, row.Name, StringComparison.Ordinal))
            await vm.RenameDatabaseAsync(row, name);
    }

    private async void OnDeleteDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (Context() is not (var vm, var top) || top is not Window owner
            || sender is not Button { CommandParameter: DatabaseRowViewModel row })
            return;

        // A whole ledger at once, and the file goes with it — nothing here can bring it back.
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            "Delete database",
            $"Delete '{row.Name}' and everything in it? The file is removed from disk and this cannot be undone.",
            "Delete");
        if (confirmed)
            await vm.DeleteDatabaseAsync(row);
    }

    /// <summary>Replace mode drops existing data, so it gets a confirmation of its own.</summary>
    private async Task<bool> ConfirmImportAsync(SettingsViewModel vm, string path)
    {
        if (vm.SelectedMode != Core.Abstractions.ImportMode.Replace)
            return true;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return true;

        return await ConfirmDialog.ShowAsync(
            owner,
            "Replace all data",
            $"Importing {Path.GetFileName(path)} in replace mode deletes every existing entry first. Continue?",
            "Replace");
    }

    private (SettingsViewModel Vm, TopLevel Top)? Context() =>
        DataContext is SettingsViewModel vm && TopLevel.GetTopLevel(this) is { } top ? (vm, top) : null;

    private static async Task<string?> SaveAsync(
        TopLevel top, string title, string suggestedName, FilePickerFileType type)
    {
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [type],
        });
        return file?.TryGetLocalPath();
    }

    private static async Task<string?> OpenAsync(TopLevel top, string title, FilePickerFileType type)
    {
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [type],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
