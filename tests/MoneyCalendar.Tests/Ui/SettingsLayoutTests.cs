using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MoneyCalendar.App.ViewModels;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.App.Views;

namespace MoneyCalendar.Tests.Ui;

/// <summary>
/// Where things sit in Settings matters: sample data is a development convenience, not part of
/// keeping your own books, so it lives behind a section that starts closed.
/// </summary>
[Collection("Headless")]
public class SettingsLayoutTests(HeadlessSessionFixture fixture)
{
    private Task Run(Func<Task> test) => fixture.Session.Dispatch(async () =>
    {
        await test();
        return true;
    }, CancellationToken.None);

    private static SettingsView Show(SettingsViewModel page)
    {
        var view = new SettingsView { DataContext = page };
        var window = new Window { Content = view, Width = 1000, Height = 900 };
        window.Show();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (view.GetVisualDescendants().OfType<Expander>().Any())
                break;

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        return view;
    }

    private static Expander DeveloperSection(SettingsView view) =>
        view.GetVisualDescendants().OfType<Expander>().Single(e => Equals(e.Header, "Developer"));

    [Fact]
    public Task The_developer_section_starts_collapsed() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        Assert.False(DeveloperSection(Show(settings)).IsExpanded);
    });

    [Fact]
    public Task Load_sample_data_lives_inside_it() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();
        var view = Show(settings);
        var developer = DeveloperSection(view);

        // Expanding realizes the content, which is where the button has to be.
        developer.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var sampleButtons = view.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => Equals(b.Content, "Load sample data"))
            .ToList();

        Assert.Single(sampleButtons);
        Assert.Contains(developer.GetVisualDescendants(), v => ReferenceEquals(v, sampleButtons[0]));

        // The destructive buttons stay out in the open, where a user would look for them.
        foreach (var label in new[] { "Delete all transactions", "Delete all data" })
        {
            var deleteButton = Assert.Single(
                view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, label));
            Assert.DoesNotContain(developer.GetVisualDescendants(), v => ReferenceEquals(v, deleteButton));
        }
    });

    [Fact]
    public Task The_database_list_marks_the_one_in_use() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        var row = Assert.Single(settings.DatabaseRows);
        Assert.True(row.IsCurrent);
        Assert.False(row.IsNotCurrent);
        Assert.Equal(settings.CurrentDatabaseName, row.Name);
    });

    [Fact]
    public Task Selecting_a_database_moves_the_whole_app_and_is_remembered() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        await host.Get<IAccountRepository>().AddAsync(
            new Account { Name = "Everyday Checking", Type = AccountType.Checking }, CancellationToken.None);
        await settings.ReloadAsync();
        Assert.Equal("1", settings.AccountCountText);

        await settings.CreateDatabaseAsync("Scratch");
        var scratch = settings.DatabaseRows.Single(d => d.Name == "Scratch");
        await settings.SelectDatabaseAsync(scratch);

        // The new ledger is empty, the app is pointed at it, and the choice is on disk.
        Assert.Equal("Scratch", settings.CurrentDatabaseName);
        Assert.Equal("0", settings.AccountCountText);
        Assert.Equal("Scratch", host.Settings.Current.DatabaseName);
        Assert.Contains("Scratch", settings.DatabasePathText, StringComparison.Ordinal);

        // And the one it came from still has what was put in it.
        await settings.SelectDatabaseAsync(settings.DatabaseRows.Single(d => d.Name != "Scratch"));
        Assert.Equal("1", settings.AccountCountText);
    });

    [Fact]
    public Task A_clone_is_a_copy_that_the_app_stays_off() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();
        var current = settings.DatabaseRows.Single();

        await settings.CloneDatabaseAsync(current, "Backup");

        Assert.Equal(current.Name, settings.CurrentDatabaseName);
        Assert.Equal(2, settings.DatabaseRows.Count);
        Assert.Contains(settings.DatabaseRows, d => d.Name == "Backup" && !d.IsCurrent);
    });

    [Fact]
    public Task Renaming_the_database_in_use_follows_it_into_settings() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        await settings.RenameDatabaseAsync(settings.DatabaseRows.Single(), "Household");

        Assert.Equal("Household", settings.CurrentDatabaseName);
        Assert.Equal("Household", host.Settings.Current.DatabaseName);
        Assert.True(settings.DatabaseRows.Single().IsCurrent);
    });

    [Fact]
    public Task The_database_in_use_cannot_be_deleted() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        await settings.DeleteDatabaseAsync(settings.DatabaseRows.Single());

        Assert.Contains("in use", settings.ErrorText!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(settings.DatabaseRows);
    });

    [Fact]
    public Task A_database_the_app_is_off_can_be_deleted() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        await settings.CreateDatabaseAsync("Scratch");
        await settings.DeleteDatabaseAsync(settings.DatabaseRows.Single(d => d.Name == "Scratch"));

        Assert.Single(settings.DatabaseRows);
        Assert.Contains("Deleted Scratch", settings.DatabaseStatusText!, StringComparison.Ordinal);
    });

    [Fact]
    public Task A_name_that_is_not_a_file_name_is_reported_not_thrown() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        await settings.CreateDatabaseAsync("books/2026");

        Assert.NotNull(settings.ErrorText);
        Assert.Single(settings.DatabaseRows);
    });

    [Fact]
    public Task Loading_sample_data_still_works_from_there() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();
        Assert.Equal("0", settings.TransactionCountText);

        // The demo ledger brings its own accounts, so an empty database is all it needs.
        Assert.True(settings.CanLoadSampleData);
        await settings.LoadSampleDataCommand.ExecuteAsync(null);

        Assert.NotEqual("0", settings.TransactionCountText);
        Assert.Equal("6", settings.AccountCountText);
        Assert.Contains("sample", settings.StatusText!, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public Task A_ledger_with_data_in_it_refuses_the_sample() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        // One account is enough: the sample would add its own beside it and there would be no
        // telling the invented entries from real ones afterwards.
        await host.Get<IAccountRepository>().AddAsync(
            new Account { Name = "Everyday Checking", Type = AccountType.Checking },
            CancellationToken.None);
        await settings.ReloadAsync();

        Assert.False(settings.CanLoadSampleData);
        await settings.LoadSampleDataCommand.ExecuteAsync(null);

        Assert.Contains("not empty", settings.ErrorText!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0", settings.TransactionCountText);
        Assert.Equal("1", settings.AccountCountText);
    });

    [Fact]
    public Task Loading_it_twice_is_refused_the_second_time() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        await settings.LoadSampleDataCommand.ExecuteAsync(null);
        var afterFirst = settings.TransactionCountText;

        await settings.LoadSampleDataCommand.ExecuteAsync(null);

        Assert.Contains("not empty", settings.ErrorText!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(afterFirst, settings.TransactionCountText);
    });
}
