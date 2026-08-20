using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MoneyCalendar.App.ViewModels;
using MoneyCalendar.App.Views;
using MoneyCalendar.Core.Abstractions;

namespace MoneyCalendar.Tests.Ui;

/// <summary>
/// The two delete buttons are the irreversible ones in the app, so both are red and neither
/// fires until the word is typed — unless there is nothing to delete in the first place.
/// </summary>
[Collection("Headless")]
public class DeleteAllDataTests(HeadlessSessionFixture fixture)
{
    private Task Run(Func<Task> test) => fixture.Session.Dispatch(async () =>
    {
        await test();
        return true;
    }, CancellationToken.None);

    private static Button DeleteButton(SettingsViewModel page, string content = "Delete all data") =>
        DeleteButtons(page).FirstOrDefault(b => Equals(b.Content, content))
        ?? throw new InvalidOperationException(content + " never appeared.");

    /// <summary>Both destructive buttons, in the order the Data section lays them out.</summary>
    private static IReadOnlyList<Button> DeleteButtons(SettingsViewModel page)
    {
        var view = new SettingsView { DataContext = page };
        var window = new Window { Content = view, Width = 1000, Height = 900 };
        window.Show();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Where(b => b.Content is string text && text.StartsWith("Delete all", StringComparison.Ordinal))
                .ToList();
            if (buttons.Count >= 2)
                return buttons;

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        throw new InvalidOperationException("The delete buttons never appeared.");
    }

    [Fact]
    public Task Both_buttons_are_named_and_styled_as_destructive() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        Assert.All(DeleteButtons(settings), b => Assert.Contains("danger", b.Classes));
        Assert.Contains("danger", DeleteButton(settings, "Delete all transactions").Classes);
    });

    [Fact]
    public Task Transactions_is_offered_before_data() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        // The narrower delete comes first, so the wider one is not the reflexive click.
        var labels = DeleteButtons(settings).Select(b => b.Content as string).ToList();
        Assert.Equal(new[] { "Delete all transactions", "Delete all data" }, labels);
    });

    [Fact]
    public void The_word_has_to_match_before_deleting()
    {
        var confirm = new DeleteDataConfirmViewModel(95);

        Assert.False(confirm.CanDelete);

        confirm.Typed = "del";
        Assert.False(confirm.CanDelete);

        confirm.Typed = "remove";
        Assert.False(confirm.CanDelete);

        // Case and stray spaces are forgiven; the word is not.
        confirm.Typed = " Delete ";
        Assert.True(confirm.CanDelete);

        confirm.Typed = "delete";
        Assert.True(confirm.CanDelete);
    }

    [Fact]
    public void The_warning_says_what_goes_and_what_stays()
    {
        var confirm = new DeleteDataConfirmViewModel(95);

        Assert.Equal("Delete all transactions", confirm.Heading);
        Assert.Contains("95 transactions", confirm.Warning, StringComparison.Ordinal);
        Assert.Contains("cannot be undone", confirm.Warning, StringComparison.Ordinal);
        Assert.Contains("Accounts, categories and settings are kept", confirm.ScopeText, StringComparison.Ordinal);
        Assert.Contains("backup", confirm.BackupAdvice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete", confirm.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_all_data_says_the_accounts_go_too()
    {
        var confirm = new DeleteDataConfirmViewModel(95, DeleteScope.Everything, accountCount: 6);

        Assert.Equal("Delete all data", confirm.Heading);
        Assert.Contains("95 transactions", confirm.Warning, StringComparison.Ordinal);
        Assert.Contains("6 accounts", confirm.Warning, StringComparison.Ordinal);
        Assert.Contains("cannot be undone", confirm.Warning, StringComparison.Ordinal);
        Assert.Contains("Categories and settings are kept", confirm.ScopeText, StringComparison.Ordinal);
        Assert.DoesNotContain("Accounts", confirm.ScopeText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_word_is_required_for_the_wider_delete_too()
    {
        var confirm = new DeleteDataConfirmViewModel(95, DeleteScope.Everything, accountCount: 6);

        Assert.False(confirm.CanDelete);
        confirm.Typed = "delete";
        Assert.True(confirm.CanDelete);
    }

    [Fact]
    public Task An_empty_ledger_skips_the_confirmation() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        Assert.Equal(0, settings.TransactionCount);
        settings.ReportNothingToDelete();
        Assert.Equal("There is nothing to delete.", settings.StatusText);
    });

    [Fact]
    public Task Confirming_wipes_the_transactions_and_keeps_the_rest() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();
        Assert.True(settings.TransactionCount > 0);

        await settings.ClearTransactionsCommand.ExecuteAsync(null);

        Assert.Equal(0, await host.Get<IEntryRepository>().CountAsync(CancellationToken.None));
        Assert.Equal("6", settings.AccountCountText);
        Assert.NotEmpty(await host.Get<ICategoryRepository>().GetAllAsync(CancellationToken.None));
        Assert.Contains("Accounts and categories were kept", settings.StatusText!, StringComparison.Ordinal);
    });

    [Fact]
    public Task Deleting_all_data_takes_the_accounts_with_it() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();
        Assert.True(settings.TransactionCount > 0);
        Assert.Equal(6, settings.AccountCount);

        await settings.ClearAllDataCommand.ExecuteAsync(null);

        Assert.Equal(0, await host.Get<IEntryRepository>().CountAsync(CancellationToken.None));
        Assert.Empty(await host.Get<IAccountRepository>().GetAllAsync(CancellationToken.None));
        Assert.Equal("0", settings.AccountCountText);
        Assert.NotEmpty(await host.Get<ICategoryRepository>().GetAllAsync(CancellationToken.None));
        Assert.Contains("Categories were kept", settings.StatusText!, StringComparison.Ordinal);
    });
}
