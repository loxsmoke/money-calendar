using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MoneyCalendar.App.ViewModels;
using MoneyCalendar.App.Views;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.Tests.Ui;

/// <summary>
/// Clicking a column header sorts through the DataGrid's collection view, so what matters is
/// that every sortable column points at a value rather than at the text it renders — sorting
/// "Aug 18, 2026" or "-$9.00" as strings gives nonsense.
/// </summary>
[Collection("Headless")]
public class ListSortingTests(HeadlessSessionFixture fixture)
{
    private Task Run(Func<Task> test) => fixture.Session.Dispatch(async () =>
    {
        await test();
        return true;
    }, CancellationToken.None);

    /// <summary>
    /// Shows the view and waits for its grid to be realized. The template does not always
    /// materialize within the first layout pass, so this pumps the dispatcher rather than
    /// assuming it is there.
    /// </summary>
    private static DataGrid GridOf(Control view, PageViewModel page)
    {
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        view.DataContext = page;
        window.Show();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault() is { } grid)
                return grid;

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        throw new InvalidOperationException($"No DataGrid appeared in {view.GetType().Name}.");
    }

    private static IReadOnlyList<object> Sorted(
        IEnumerable<EntryRowViewModel> rows, string path, ListSortDirection direction)
    {
        var view = new DataGridCollectionView(rows.ToList());
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(path, direction));
        return view.Cast<object>().ToList();
    }

    [Fact]
    public Task Every_sortable_column_sorts_by_a_value_not_by_its_text() => Run(async () =>
    {
        using var host = new TestHost();
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();
        var grid = GridOf(new LedgerView(), expenses);

        Assert.True(grid.CanUserSortColumns);
        var paths = grid.Columns
            .Where(c => c.CanUserSort)
            .Select(c => c.SortMemberPath)
            .ToList();

        // Date and Amount sort by the underlying value; the other columns are plain text.
        Assert.Contains("Date", paths);
        Assert.Contains("SortAmount", paths);
        Assert.Contains("IsRecurring", paths);
        Assert.Contains("CategoryName", paths);
        Assert.DoesNotContain("DateText", paths);
        Assert.DoesNotContain("AmountText", paths);
        // The button column is not sortable.
        Assert.Contains(grid.Columns, c => !c.CanUserSort);
    });

    [Fact]
    public Task The_summary_list_sorts_too() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();
        var grid = GridOf(new SummaryView(), summary);

        Assert.True(grid.CanUserSortColumns);
        var paths = grid.Columns.Select(c => c.SortMemberPath).ToList();
        Assert.Contains("Date", paths);
        Assert.Contains("SortAmount", paths);
        Assert.Contains("CategoryName", paths);
    });

    [Fact]
    public Task Sorting_by_date_orders_chronologically() => Run(async () =>
    {
        using var host = new TestHost();
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();

        var ascending = Sorted(expenses.Rows, "Date", ListSortDirection.Ascending)
            .Cast<EntryRowViewModel>()
            .Select(r => r.Date)
            .ToList();

        Assert.Equal(ascending.OrderBy(d => d), ascending);
        // Sorting the rendered text instead would put "Aug 8" after "Aug 18".
        Assert.NotEqual(
            expenses.Rows.OrderBy(r => r.DateText, StringComparer.CurrentCulture).Select(r => r.Date),
            ascending);
    });

    [Fact]
    public Task Sorting_by_amount_orders_numerically() => Run(async () =>
    {
        using var host = new TestHost();
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();

        var ascending = Sorted(expenses.Rows, "SortAmount", ListSortDirection.Ascending)
            .Cast<EntryRowViewModel>()
            .Select(r => r.SortAmount)
            .ToList();

        Assert.Equal(ascending.OrderBy(a => a), ascending);
        // Expenses are negative, so the biggest spend sorts first ascending.
        Assert.Equal(ascending.Min(), ascending[0]);
    });

    [Fact]
    public Task A_chosen_sort_survives_a_reload() => Run(async () =>
    {
        using var host = new TestHost();
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();
        var grid = GridOf(new LedgerView(), expenses);

        grid.CollectionView.SortDescriptions.Add(
            DataGridSortDescription.FromPath("SortAmount", ListSortDirection.Ascending));

        // Changing the range refills the same collection; the sort belongs to the view, so it
        // has to still be there afterwards.
        expenses.SelectedBackRange = expenses.BackRangeOptions.Single(o => o.Label == "Last 2 months");
        await expenses.ReloadAsync();

        Assert.Single(grid.CollectionView.SortDescriptions);
        var shown = grid.CollectionView.Cast<EntryRowViewModel>().Select(r => r.SortAmount).ToList();
        Assert.Equal(shown.OrderBy(a => a), shown);
    });

    [Fact]
    public Task Sorting_by_repeating_groups_the_series() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var account = await host.Get<IAccountRepository>().AddAsync(
            new Account { Name = "Everyday", Type = AccountType.Checking }, CancellationToken.None);
        var today = host.Get<IClock>().Today;
        var entries = host.Get<IEntryRepository>();

        await entries.AddAsync(
            new Entry
            {
                Date = new DateOnly(today.Year, today.Month, 4), Amount = 50m, Kind = EntryKind.Income,
                CategoryId = DefaultCategories.Tips, CurrencyCode = "USD", AccountId = account.Id,
            },
            CancellationToken.None);
        await entries.AddAsync(
            new Entry
            {
                Date = new DateOnly(today.Year, today.Month, 1), Amount = 900m, Kind = EntryKind.Income,
                CategoryId = DefaultCategories.Salary, CurrencyCode = "USD", AccountId = account.Id,
                Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 2,
            },
            CancellationToken.None);

        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var descending = Sorted(income.Rows, "IsRecurring", ListSortDirection.Descending)
            .Cast<EntryRowViewModel>()
            .ToList();

        Assert.True(descending[0].IsRecurring);
        Assert.False(descending[^1].IsRecurring);
    });
}
