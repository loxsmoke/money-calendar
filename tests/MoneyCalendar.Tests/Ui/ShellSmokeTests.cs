using MoneyCalendar.App.Services;
using MoneyCalendar.App.ViewModels;
using MoneyCalendar.App.Views;
using MoneyCalendar.Core.Abstractions;
using MoneyCalendar.Core.Entities;
using MoneyCalendar.Core.Seed;

namespace MoneyCalendar.Tests.Ui;

[Collection("Headless")]
public class ShellSmokeTests(HeadlessSessionFixture fixture)
{
    // Dispatch(Func&lt;Task&gt;) binds to the Func&lt;TResult&gt; overload and hands back Task&lt;Task&gt;:
    // awaiting it would only wait for the lambda's first await, so a later failing assertion
    // would pass silently. Returning a value picks the Func&lt;Task&lt;T&gt;&gt; overload, which
    // really does await the whole body.
    private Task Run(Func<Task> test) => fixture.Session.Dispatch(async () =>
    {
        await test();
        return true;
    }, CancellationToken.None);

    /// <summary>Income now requires an account, so seedless hosts need one before saving.</summary>
    private static Task<Account> AddCheckingAsync(TestHost host, string name = "Everyday") =>
        host.Get<IAccountRepository>().AddAsync(
            new Account { Name = name, Type = AccountType.Checking }, CancellationToken.None);

    [Fact]
    public Task Shell_opens_on_Summary_with_every_section() => Run(async () =>
    {
        using var host = new TestHost();
        var shell = host.Get<MainWindowViewModel>();
        var window = new MainWindow { DataContext = shell };
        window.Show();

        // Accounts sits between Expenses and Settings; About closes the list.
        Assert.Equal(
            new[] { "Summary", "Income", "Expenses", "Accounts", "Settings", "About" },
            shell.NavItems.Select(i => i.Label));
        Assert.Equal("Summary", shell.SelectedNavItem?.Label);

        await host.Get<SummaryViewModel>().EnsureLoadedAsync();
        Assert.IsType<SummaryViewModel>(shell.CurrentPage);

        window.Close();
    });

    [Fact]
    public Task Every_section_loads_without_error() => Run(async () =>
    {
        using var host = new TestHost();
        PageViewModel[] pages =
        [
            host.Get<SummaryViewModel>(),
            host.Get<IncomeViewModel>(),
            host.Get<ExpensesViewModel>(),
            host.Get<AccountsViewModel>(),
            host.Get<SettingsViewModel>(),
            host.Get<AboutViewModel>(),
        ];

        foreach (var page in pages)
        {
            await page.ReloadAsync();
            Assert.NotEqual(PageState.Error, page.State);
            Assert.Null(page.ErrorMessage);
        }
    });

    [Fact]
    public Task Summary_builds_chart_series_and_list() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();

        // Income bars, expense bars, balance line — no budget line.
        Assert.Equal(
            new[] { "Income", "Expenses", "Balance" },
            summary.ChartSeries.Select(s => s.Name));
        Assert.Single(summary.ChartXAxes);
        // Bars and balance share one axis, so their heights compare directly.
        Assert.Single(summary.ChartYAxes);
        Assert.All(
            summary.ChartSeries.OfType<LiveChartsCore.Kernel.Sketches.ICartesianSeries>(),
            s => Assert.Equal(0, s.ScalesYAt));

        // The balance is a staircase, not a slope, and it spans the whole plot boundary to
        // boundary. Every column is one flat run at the balance that bucket opened with, so the
        // risers stand between columns and the first column shows the balance carried into the
        // range rather than one that only exists once the first day is over.
        var balance = Assert.IsType<
            LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint>>(
            summary.ChartSeries.Single(s => s.Name == "Balance"));
        var points = balance.Values!.ToList();
        var columns = summary.ChartXAxes[0].Labels!.Count;

        Assert.Equal((columns * 2) + 1, points.Count);
        for (var column = 0; column < columns; column++)
        {
            var (left, right) = (points[column * 2], points[(column * 2) + 1]);
            Assert.Equal(column - 0.5, left.X);
            Assert.Equal(column + 0.5, right.X);
            Assert.Equal(left.Y, right.Y);
        }

        // The last bucket's closing balance is the final riser, on the right-hand boundary.
        Assert.Equal(columns - 0.5, points[^1].X);
        Assert.Contains("Balance", summary.BalanceLineText, StringComparison.Ordinal);
        Assert.NotEmpty(summary.Entries);
    });

    [Fact]
    public Task Range_defaults_to_this_month_at_both_ends() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();

        Assert.Equal(
            new[] { "This month", "Last 2 months", "Last 3 months", "Custom" },
            summary.BackRangeOptions.Select(o => o.Label));
        Assert.Equal(
            new[] { "This month", "Next 2 months", "Next 3 months", "Custom" },
            summary.ForwardRangeOptions.Select(o => o.Label));

        Assert.Equal("This month", summary.SelectedBackRange?.Label);
        Assert.Equal("This month", summary.SelectedForwardRange?.Label);

        var today = host.Get<IClock>().Today;
        Assert.Equal(new DateOnly(today.Year, today.Month, 1), summary.CurrentRange.From);
        Assert.Equal(
            new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            summary.CurrentRange.To);
    });

    [Fact]
    public Task Back_dropdown_moves_the_start_and_leaves_the_end_alone() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();
        var today = host.Get<IClock>().Today;
        var end = summary.CurrentRange.To;

        summary.SelectedBackRange = summary.BackRangeOptions.Single(o => o.Label == "Last 3 months");
        await summary.ReloadAsync();

        // Three months back starts two whole months before this one.
        var expected = today.AddMonths(-2);
        Assert.Equal(new DateOnly(expected.Year, expected.Month, 1), summary.CurrentRange.From);
        Assert.Equal(end, summary.CurrentRange.To);
        Assert.Equal("This month", summary.SelectedForwardRange?.Label);
    });

    [Fact]
    public Task Forward_dropdown_moves_the_end_and_leaves_the_start_alone() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();
        var today = host.Get<IClock>().Today;
        var start = summary.CurrentRange.From;

        summary.SelectedForwardRange = summary.ForwardRangeOptions.Single(o => o.Label == "Next 2 months");
        await summary.ReloadAsync();

        var expected = today.AddMonths(1);
        Assert.Equal(
            new DateOnly(expected.Year, expected.Month, DateTime.DaysInMonth(expected.Year, expected.Month)),
            summary.CurrentRange.To);
        Assert.Equal(start, summary.CurrentRange.From);
        Assert.Equal("This month", summary.SelectedBackRange?.Label);
    });

    [Fact]
    public Task Both_dropdowns_together_span_back_and_forward() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();
        var today = host.Get<IClock>().Today;

        summary.SelectedBackRange = summary.BackRangeOptions.Single(o => o.Label == "Last 3 months");
        summary.SelectedForwardRange = summary.ForwardRangeOptions.Single(o => o.Label == "Next 3 months");
        await summary.ReloadAsync();

        var first = today.AddMonths(-2);
        var last = today.AddMonths(2);
        Assert.Equal(new DateOnly(first.Year, first.Month, 1), summary.CurrentRange.From);
        Assert.Equal(
            new DateOnly(last.Year, last.Month, DateTime.DaysInMonth(last.Year, last.Month)),
            summary.CurrentRange.To);
        // Five whole months still fits inside the cap, so nothing is trimmed.
        Assert.Null(summary.RangeNotice);
    });

    [Fact]
    public Task Editing_a_date_switches_only_that_end_to_custom() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();

        summary.StartDate = new DateTimeOffset(new DateTime(2026, 3, 9), TimeSpan.Zero);
        await summary.ReloadAsync();

        Assert.Equal("Custom", summary.SelectedBackRange?.Label);
        Assert.Equal("This month", summary.SelectedForwardRange?.Label);
        Assert.Equal(new DateOnly(2026, 3, 9), summary.CurrentRange.From);

        summary.EndDate = new DateTimeOffset(new DateTime(2026, 4, 20), TimeSpan.Zero);
        await summary.ReloadAsync();

        Assert.Equal("Custom", summary.SelectedForwardRange?.Label);
        Assert.Equal(new DateOnly(2026, 4, 20), summary.CurrentRange.To);
    });

    [Fact]
    public Task A_hand_picked_range_beyond_the_cap_is_trimmed() => Run(async () =>
    {
        using var host = new TestHost();
        var summary = host.Get<SummaryViewModel>();
        await summary.ReloadAsync();

        summary.StartDate = new DateTimeOffset(new DateTime(2026, 1, 1), TimeSpan.Zero);
        summary.EndDate = new DateTimeOffset(new DateTime(2026, 12, 31), TimeSpan.Zero);
        await summary.ReloadAsync();

        Assert.Equal(186, summary.CurrentRange.DayCount);
        Assert.NotNull(summary.RangeNotice);
        Assert.Equal("Custom", summary.SelectedBackRange?.Label);
        Assert.Equal("Custom", summary.SelectedForwardRange?.Label);
    });

    [Fact]
    public Task Adding_an_expense_through_the_editor_shows_up_in_the_list() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var accountRepo = host.Get<IAccountRepository>();
        await accountRepo.AddAsync(
            new Account { Name = "Everyday Checking", Type = AccountType.Checking, Last4 = "1042" },
            CancellationToken.None);
        await accountRepo.AddAsync(
            new Account { Name = "Sapphire Visa", Type = AccountType.Credit, Last4 = "4417" },
            CancellationToken.None);

        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();
        Assert.Empty(expenses.Rows);

        var editor = await expenses.CreateEditorAsync(null);
        editor.AmountText = "125.50";
        editor.SelectedCategory = editor.Categories.First(c => c.Name == "Credit card");
        editor.SelectedAccount = editor.Accounts.Single(a => a.Label == "Everyday Checking");
        editor.SelectedToAccount = editor.ToAccounts.Single(a => a.Label == "Sapphire Visa");

        Assert.True(await expenses.SaveEditorAsync(editor));

        var row = Assert.Single(expenses.Rows);
        Assert.Equal("Credit card", row.CategoryName);
        // The list shows the flow, from account to account.
        // Only the destination keeps its digits; the source is named alone.
        Assert.Equal("Everyday Checking → Sapphire Visa ••••4417", row.AccountText);
        Assert.False(row.IsIncome);
    });

    [Fact]
    public Task Editor_rejects_a_missing_or_zero_amount() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var editor = await income.CreateEditorAsync(null);
        editor.AmountText = "not a number";
        Assert.False(await income.SaveEditorAsync(editor));
        Assert.NotNull(editor.ErrorText);

        editor.AmountText = "0";
        Assert.False(await income.SaveEditorAsync(editor));

        editor.AmountText = "1200";
        Assert.True(await income.SaveEditorAsync(editor));
        Assert.Single(income.Rows);
    });

    [Fact]
    public Task Categories_are_managed_from_Settings() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        Assert.Equal(
            new[] { "Income types", "Expense categories" },
            settings.CategoryGroups.Select(g => g.Title));

        // Nothing has been added yet, so the custom panel has nothing to group.
        Assert.False(settings.HasCustomCategories);
        Assert.Empty(settings.CustomCategoryGroups);

        var editor = await settings.CreateCategoryEditorAsync(null);
        editor.Name = "Childcare";
        editor.SelectedKind = editor.Kinds.Single(k => k.Kind == EntryKind.Expense);
        editor.PickColor(editor.Colors[3]);
        Assert.True(await settings.SaveCategoryAsync(editor));

        // It lands in the custom panel, and only there.
        Assert.True(settings.HasCustomCategories);
        var custom = Assert.Single(settings.CustomCategoryGroups);
        Assert.Equal("Expense categories", custom.Title);
        Assert.Equal("Childcare", Assert.Single(custom.Categories).Name);
        Assert.DoesNotContain(
            settings.CategoryGroups.SelectMany(g => g.Categories), c => c.Name == "Childcare");

        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();
        Assert.Contains(expenses.CategoryChoices, c => c.Name == "Childcare");
        Assert.DoesNotContain(host.Get<IncomeViewModel>().CategoryChoices, c => c.Name == "Childcare");
    });

    [Fact]
    public Task Renaming_and_recoloring_a_category_sticks() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        var rent = settings.CategoryGroups
            .SelectMany(g => g.Categories)
            .Single(c => c.Name == "Rent");
        var editor = await settings.CreateCategoryEditorAsync(rent);

        // A built-in category keeps its side of the app but can be renamed and recolored.
        Assert.False(editor.CanChangeKind);
        editor.Name = "Housing";
        editor.PickColor(editor.Colors.First(c => c.Hex != rent.Category.ColorHex));
        Assert.True(await settings.SaveCategoryAsync(editor));

        var stored = (await host.Get<ICategoryRepository>().GetAllAsync(CancellationToken.None))
            .Single(c => c.Id == rent.Id);
        Assert.Equal("Housing", stored.Name);
        Assert.NotEqual(rent.Category.ColorHex, stored.ColorHex);
    });

    [Fact]
    public Task Built_in_and_in_use_categories_survive_a_delete() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();
        var before = settings.CategoryGroups.Sum(g => g.Categories.Count);

        var builtIn = settings.CategoryGroups.SelectMany(g => g.Categories).First(c => c.IsSystem);
        await settings.DeleteCategoryAsync(builtIn);
        Assert.Contains("built-in", settings.CategoryStatusText!, StringComparison.Ordinal);
        Assert.Equal(before, settings.CategoryGroups.Sum(g => g.Categories.Count));

        // A custom one with nothing filed under it does go.
        var editor = await settings.CreateCategoryEditorAsync(null);
        editor.Name = "Temporary";
        await settings.SaveCategoryAsync(editor);
        var custom = settings.CustomCategoryGroups.SelectMany(g => g.Categories).Single(c => c.Name == "Temporary");

        await settings.DeleteCategoryAsync(custom);
        Assert.DoesNotContain(
            settings.CustomCategoryGroups.SelectMany(g => g.Categories), c => c.Name == "Temporary");
        Assert.False(settings.HasCustomCategories);
    });

    [Fact]
    public Task Category_groups_start_folded_for_built_ins_and_open_for_custom_ones() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        // The built-in list is long and rarely touched, so it does not unroll on arrival.
        Assert.All(settings.CategoryGroups, g => Assert.False(g.IsExpanded));

        var editor = await settings.CreateCategoryEditorAsync(null);
        editor.Name = "Childcare";
        await settings.SaveCategoryAsync(editor);
        Assert.All(settings.CustomCategoryGroups, g => Assert.True(g.IsExpanded));

        // Opening a group survives the reload that every category edit triggers.
        var expenses = settings.CategoryGroups.Single(g => g.Title == "Expense categories");
        expenses.IsExpanded = true;

        editor = await settings.CreateCategoryEditorAsync(null);
        editor.Name = "Pet care";
        await settings.SaveCategoryAsync(editor);

        Assert.True(settings.CategoryGroups.Single(g => g.Title == "Expense categories").IsExpanded);
        Assert.False(settings.CategoryGroups.Single(g => g.Title == "Income types").IsExpanded);
    });

    [Fact]
    public Task Income_entries_pick_the_account_the_money_lands_in() => Run(async () =>
    {
        using var host = new TestHost();
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var editor = await income.CreateEditorAsync(null);
        Assert.True(editor.ShowAccountPicker);
        Assert.Contains(editor.Accounts, a => a.Label == "Demo Checking");
        // Pre-selected, because income always has to land somewhere.
        Assert.NotNull(editor.SelectedAccount);

        editor.AmountText = "1500";
        editor.SelectedAccount = editor.Accounts.Single(a => a.Label == "Demo Savings");
        Assert.True(await income.SaveEditorAsync(editor));

        var row = income.Rows.First(r => r.AmountText.Contains("1,500", StringComparison.Ordinal));
        Assert.Contains("Demo Savings", row.AccountText!, StringComparison.Ordinal);
    });

    [Fact]
    public Task The_income_account_picker_lists_only_income_accounts() => Run(async () =>
    {
        using var host = new TestHost();
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var editor = await income.CreateEditorAsync(null);

        // Checking, savings, investment and other income are where money comes in.
        Assert.Equal(
            new[] { "Demo Checking", "Demo Savings" },
            editor.Accounts.Select(a => a.Label));
        // Credit and mortgage are expense accounts, and there is no "no account" escape hatch.
        Assert.DoesNotContain(editor.Accounts, a => a.Label == "Demo Visa");
        Assert.DoesNotContain(editor.Accounts, a => a.Label == "Demo Mortgage");
        Assert.DoesNotContain(editor.Accounts, a => a.Label.Contains("No account", StringComparison.Ordinal));
    });

    [Fact]
    public Task Income_cannot_be_saved_without_an_account() => Run(async () =>
    {
        using var host = new TestHost();
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();
        var before = income.Rows.Count;

        var editor = await income.CreateEditorAsync(null);
        editor.AmountText = "900";
        editor.SelectedAccount = null;

        Assert.False(await income.SaveEditorAsync(editor));
        Assert.Equal("Pick the account this income goes into.", editor.ErrorText);
        Assert.Equal(before, income.Rows.Count);

        editor.SelectedAccount = editor.Accounts[0];
        Assert.True(await income.SaveEditorAsync(editor));
    });

    [Fact]
    public Task Without_any_income_account_adding_income_asks_for_one() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        // A credit card is an expense account, so it does not make income postable.
        await host.Get<IAccountRepository>().AddAsync(
            new Account { Name = "Some Card", Type = AccountType.Credit }, CancellationToken.None);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        Assert.True(income.RequiresAccounts);
        Assert.Contains("income account", income.MissingAccountTitle, StringComparison.OrdinalIgnoreCase);
        // The message names every type that counts as an income account.
        foreach (var type in AccountTypes.IncomeTypes)
            Assert.Contains(AccountTypes.Label(type), income.MissingAccountMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Credit", income.MissingAccountMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Mortgage", income.MissingAccountMessage, StringComparison.Ordinal);

        // The message offers a way out, straight to the Accounts section.
        income.OpenAccounts();
        Assert.Equal(PageKey.Accounts, host.Get<INavigationService>().CurrentKey);
    });

    [Fact]
    public Task Adding_an_income_account_clears_the_block() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();
        Assert.True(income.RequiresAccounts);

        await AddCheckingAsync(host, "Everyday");
        await income.ReloadAsync();

        Assert.False(income.RequiresAccounts);
        var editor = await income.CreateEditorAsync(null);
        Assert.True(editor.HasAccounts);
    });

    [Fact]
    public Task An_expense_without_an_expense_account_asks_for_one() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();

        // The income side exists, so only the expense side is named.
        Assert.True(expenses.RequiresAccounts);
        Assert.Contains("Credit", expenses.MissingAccountMessage, StringComparison.Ordinal);
        Assert.Contains("Mortgage", expenses.MissingAccountMessage, StringComparison.Ordinal);
        Assert.Contains("Other expense", expenses.MissingAccountMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Income accounts are", expenses.MissingAccountMessage, StringComparison.Ordinal);

        await host.Get<IAccountRepository>().AddAsync(
            new Account { Name = "Some Card", Type = AccountType.Credit }, CancellationToken.None);
        await expenses.ReloadAsync();

        Assert.False(expenses.RequiresAccounts);
        var editor = await expenses.CreateEditorAsync(null);
        editor.AmountText = "75";
        Assert.True(await expenses.SaveEditorAsync(editor));
    });

    [Fact]
    public Task With_no_accounts_at_all_the_expense_message_names_both_sides() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();

        Assert.True(expenses.RequiresAccounts);
        Assert.Contains("Income accounts are", expenses.MissingAccountMessage, StringComparison.Ordinal);
        Assert.Contains("Expense accounts are", expenses.MissingAccountMessage, StringComparison.Ordinal);
    });

    [Fact]
    public Task Expenses_pick_a_from_account_and_a_to_account() => Run(async () =>
    {
        using var host = new TestHost();
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();

        var editor = await expenses.CreateEditorAsync(null);

        Assert.True(editor.ShowAccountPicker);
        Assert.True(editor.ShowToAccountPicker);
        Assert.Equal("From account", editor.AccountLabel);

        // From: income accounts only. To: expense accounts only. Both come back grouped by
        // type then name, which is the order the pickers show.
        Assert.Equal(
            new[] { "Demo Checking", "Demo Savings" },
            editor.Accounts.Select(a => a.Label));
        Assert.Equal(
            new[] { "Demo Mastercard", "Demo Visa", "Demo Mortgage", "Bill payments" },
            editor.ToAccounts.Select(a => a.Label));
    });

    [Fact]
    public Task An_expense_needs_both_ends_of_the_flow() => Run(async () =>
    {
        using var host = new TestHost();
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();

        var editor = await expenses.CreateEditorAsync(null);
        editor.AmountText = "60";
        editor.SelectedToAccount = null;
        Assert.False(await expenses.SaveEditorAsync(editor));
        Assert.Equal("Pick the account this expense goes to.", editor.ErrorText);

        editor.SelectedToAccount = editor.ToAccounts[0];
        editor.SelectedAccount = null;
        Assert.False(await expenses.SaveEditorAsync(editor));
        Assert.Equal("Pick the account this expense is paid from.", editor.ErrorText);

        editor.SelectedAccount = editor.Accounts[0];
        Assert.True(await expenses.SaveEditorAsync(editor));
    });

    [Fact]
    public Task A_repeating_income_shows_up_on_every_occurrence() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();
        var today = host.Get<IClock>().Today;

        var editor = await income.CreateEditorAsync(null);
        editor.Date = new DateTimeOffset(
            new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        editor.AmountText = "2450";
        editor.Repeats = true;
        editor.SelectedFrequency = editor.Frequencies.Single(f => f.Label == "Twice monthly");
        editor.SelectedDayOfMonth = editor.MonthDays.Single(d => d.Day == 15 && d.Mode == MonthDayMode.OnDay);
        editor.SelectedSecondDay = editor.SecondMonthDays.Single(d => d.Mode == MonthDayMode.LastDay);
        Assert.Contains("Twice monthly", editor.RepeatSummary, StringComparison.Ordinal);

        Assert.True(await income.SaveEditorAsync(editor));

        // One stored row, two visible occurrences in the month.
        Assert.Equal(1, await host.Get<IEntryRepository>().CountAsync(CancellationToken.None));
        Assert.Equal(2, income.Rows.Count);
        Assert.All(income.Rows, r => Assert.True(r.IsRecurring));
        Assert.Contains("Twice monthly on the 15th and the last day", income.Rows[0].RepeatText, StringComparison.Ordinal);
        Assert.Equal(
            [new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
             new DateOnly(today.Year, today.Month, 15)],
            income.Rows.Select(r => r.Date));
    });

    [Fact]
    public Task Editing_an_occurrence_edits_the_whole_series() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();
        var today = host.Get<IClock>().Today;

        var editor = await income.CreateEditorAsync(null);
        editor.Date = new DateTimeOffset(
            new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        editor.AmountText = "500";
        editor.Repeats = true;
        editor.SelectedFrequency = editor.Frequencies.Single(f => f.Label == "Weekly");
        editor.SelectedWeekday = editor.Weekdays.Single(w => w.Day == DayOfWeek.Monday);
        await income.SaveEditorAsync(editor);
        var occurrences = income.Rows.Count;
        Assert.True(occurrences >= 4);

        // Editing any occurrence opens the stored template, still starting on the 1st.
        var second = await income.CreateEditorAsync(income.Rows[1]);
        Assert.True(second.Repeats);
        Assert.Equal(1, second.Date.Day);
        second.AmountText = "650";
        Assert.True(await income.SaveEditorAsync(second));

        Assert.Equal(1, await host.Get<IEntryRepository>().CountAsync(CancellationToken.None));
        Assert.Equal(occurrences, income.Rows.Count);
        Assert.All(income.Rows, r => Assert.Contains("650", r.AmountText, StringComparison.Ordinal));
    });

    [Fact]
    public Task Ticking_repeats_reveals_monthly_on_the_first() => Run(async () =>
    {
        using var host = new TestHost();
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var editor = await income.CreateEditorAsync(null);
        Assert.False(editor.ShowDayOfMonth);

        editor.Repeats = true;

        // Monthly is the default frequency, so its day picker has to appear with it.
        Assert.Equal("Monthly", editor.SelectedFrequency.Label);
        Assert.True(editor.ShowDayOfMonth);
        Assert.False(editor.ShowSecondDay);
        Assert.False(editor.ShowWeekday);
        Assert.Equal(1, editor.SelectedDayOfMonth.Day);
        Assert.Equal("Day of month", editor.FirstDayLabel);
        Assert.Equal("Monthly on the 1st", editor.RepeatSummary);
    });

    [Fact]
    public Task Switching_frequency_swaps_the_pattern_controls() => Run(async () =>
    {
        using var host = new TestHost();
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var editor = await income.CreateEditorAsync(null);
        editor.Repeats = true;

        editor.SelectedFrequency = editor.Frequencies.Single(f => f.Label == "Twice monthly");
        Assert.True(editor.ShowDayOfMonth);
        Assert.True(editor.ShowSecondDay);
        Assert.False(editor.ShowWeekday);
        Assert.Equal("First day", editor.FirstDayLabel);

        editor.SelectedFrequency = editor.Frequencies.Single(f => f.Label == "Weekly");
        Assert.False(editor.ShowDayOfMonth);
        Assert.False(editor.ShowSecondDay);
        Assert.True(editor.ShowWeekday);

        // Unticking hides the lot again.
        editor.Repeats = false;
        Assert.False(editor.ShowWeekday);
    });

    [Fact]
    public Task An_edited_series_keeps_the_day_it_was_saved_with() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        // Start on the 1st so this month's 12th is still ahead of the series start.
        var today = host.Get<IClock>().Today;
        var editor = await income.CreateEditorAsync(null);
        editor.Date = new DateTimeOffset(
            new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        editor.AmountText = "2000";
        editor.Repeats = true;
        editor.SelectedDayOfMonth = editor.MonthDays.Single(d => d.Day == 12 && d.Mode == MonthDayMode.OnDay);
        Assert.True(await income.SaveEditorAsync(editor));
        Assert.NotEmpty(income.Rows);

        var reopened = await income.CreateEditorAsync(income.Rows[0]);

        Assert.True(reopened.Repeats);
        Assert.True(reopened.ShowDayOfMonth);
        Assert.Equal(12, reopened.SelectedDayOfMonth.Day);
    });

    [Fact]
    public Task Show_repeating_only_narrows_the_list_and_its_summary() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var today = host.Get<IClock>().Today;
        var entries = host.Get<IEntryRepository>();
        var account = (await host.Get<IAccountRepository>().GetAllAsync(CancellationToken.None))[0];

        // One-off on the 3rd, and a monthly series on the 10th.
        await entries.AddAsync(
            new Entry
            {
                Date = new DateOnly(today.Year, today.Month, 3), Amount = 90m, Kind = EntryKind.Income,
                CategoryId = DefaultCategories.Tips, CurrencyCode = "USD", AccountId = account.Id,
            },
            CancellationToken.None);
        await entries.AddAsync(
            new Entry
            {
                Date = new DateOnly(today.Year, today.Month, 1), Amount = 2000m, Kind = EntryKind.Income,
                CategoryId = DefaultCategories.Salary, CurrencyCode = "USD", AccountId = account.Id,
                Frequency = RecurrenceFrequency.Monthly, DayOfMonth = 10,
            },
            CancellationToken.None);

        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        Assert.False(income.ShowRepeatingOnly);
        Assert.Equal(2, income.Rows.Count);
        Assert.Contains("2,090", income.TotalText, StringComparison.Ordinal);

        income.ShowRepeatingOnly = true;
        await income.ReloadAsync();

        var row = Assert.Single(income.Rows);
        Assert.True(row.IsRecurring);
        // The range summary and the by-type bars follow the filter, so they agree with the list.
        Assert.Contains("2,000", income.TotalText, StringComparison.Ordinal);
        Assert.Equal("1 entry", income.CountText);
        Assert.Equal("Salary", Assert.Single(income.Breakdown).Name);

        income.ShowRepeatingOnly = false;
        await income.ReloadAsync();
        Assert.Equal(2, income.Rows.Count);
    });

    [Fact]
    public Task List_dates_drop_the_weekday() => Run(async () =>
    {
        using var host = new TestHost();
        var expenses = host.Get<ExpensesViewModel>();
        await expenses.ReloadAsync();

        var row = expenses.Rows[0];
        Assert.Equal(Format.MediumDate(row.Date), row.DateText);
        Assert.DoesNotContain(",", row.DateText.Split(',')[0], StringComparison.Ordinal);
        Assert.DoesNotContain(row.Date.DayOfWeek.ToString()[..3], row.DateText, StringComparison.Ordinal);
    });

    [Fact]
    public Task Settings_data_section_counts_what_is_stored() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        // Six seeded accounts. Most of the demo ledger is standing bills, so a repeating
        // series counts once here however many occurrences it draws elsewhere.
        Assert.Equal("6", settings.AccountCountText);
        Assert.NotEqual("0", settings.RepeatingCountText);
        Assert.Equal(
            (await host.Get<IEntryRepository>().CountAsync(CancellationToken.None))
                .ToString(System.Globalization.CultureInfo.CurrentCulture),
            settings.TransactionCountText);
        // The demo ledger files three bills under categories of its own; nothing else custom
        // ships with the app, so an install without the sample data counts none.
        Assert.Equal("3", settings.CustomCategoryCountText);

        using var bare = new TestHost(seedSampleData: false);
        var bareSettings = bare.Get<SettingsViewModel>();
        await bareSettings.ReloadAsync();
        Assert.Equal("0", bareSettings.CustomCategoryCountText);
    });

    [Fact]
    public Task A_series_that_ends_stops_producing_occurrences() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();
        var today = host.Get<IClock>().Today;
        var start = new DateOnly(today.Year, today.Month, 1);

        var editor = await income.CreateEditorAsync(null);
        editor.Date = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        editor.AmountText = "1000";
        editor.Repeats = true;
        editor.SelectedFrequency = editor.Frequencies.Single(f => f.Label == "Monthly");
        editor.Ends = true;
        editor.EndDate = new DateTimeOffset(
            start.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        Assert.Contains("until", editor.RepeatSummary, StringComparison.Ordinal);
        Assert.True(await income.SaveEditorAsync(editor));

        // Two occurrences: this month's and next month's, then nothing.
        income.SelectedForwardRange = income.ForwardRangeOptions.Single(o => o.Label == "Next 3 months");
        await income.ReloadAsync();
        Assert.Equal(2, income.Rows.Count);
        Assert.Equal([start.AddMonths(1), start], income.Rows.Select(r => r.Date));

        // Reopening the series remembers the end date.
        var reopened = await income.CreateEditorAsync(income.Rows[0]);
        Assert.True(reopened.Ends);
        Assert.Equal(start.AddMonths(1), DateOnly.FromDateTime(reopened.EndDate.Date));
    });

    [Fact]
    public Task A_series_cannot_end_before_it_starts() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var editor = await income.CreateEditorAsync(null);
        editor.AmountText = "100";
        editor.Repeats = true;
        editor.Ends = true;
        editor.EndDate = editor.Date.AddDays(-1);

        Assert.False(await income.SaveEditorAsync(editor));
        Assert.Equal("The series cannot end before it starts.", editor.ErrorText);
    });

    [Fact]
    public Task Twice_monthly_rejects_two_identical_days() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        await AddCheckingAsync(host);
        var income = host.Get<IncomeViewModel>();
        await income.ReloadAsync();

        var editor = await income.CreateEditorAsync(null);
        editor.AmountText = "100";
        editor.Repeats = true;
        editor.SelectedFrequency = editor.Frequencies.Single(f => f.Label == "Twice monthly");
        editor.SelectedDayOfMonth = editor.MonthDays.Single(d => d.Day == 10 && d.Mode == MonthDayMode.OnDay);
        editor.SelectedSecondDay = editor.SecondMonthDays.Single(d => d.Day == 10 && d.Mode == MonthDayMode.OnDay);

        Assert.False(await income.SaveEditorAsync(editor));
        Assert.NotNull(editor.ErrorText);
    });

    [Fact]
    public Task Accounts_show_the_income_received_to_date() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var clock = host.Get<IClock>();
        var accountRepo = host.Get<IAccountRepository>();
        var entries = host.Get<IEntryRepository>();
        var checking = await accountRepo.AddAsync(
            new Account { Name = "Everyday", Type = AccountType.Checking }, CancellationToken.None);
        await accountRepo.AddAsync(
            new Account { Name = "Unused", Type = AccountType.Savings }, CancellationToken.None);

        Entry Income(DateOnly date, decimal amount) => new()
        {
            Date = date, Amount = amount, Kind = EntryKind.Income,
            CategoryId = DefaultCategories.Salary, CurrencyCode = "USD", AccountId = checking.Id,
        };

        await entries.AddAsync(Income(clock.Today.AddDays(-40), 1000m), CancellationToken.None);
        await entries.AddAsync(Income(clock.Today, 500m), CancellationToken.None);
        // Future income has not been received yet, so it must not count.
        await entries.AddAsync(Income(clock.Today.AddDays(10), 900m), CancellationToken.None);

        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();

        var row = accounts.Rows.Single(r => r.Name == "Everyday");
        Assert.Equal(1500m, row.IncomeToDate);
        Assert.True(row.HasIncome);
        Assert.False(accounts.Rows.Single(r => r.Name == "Unused").HasIncome);
        Assert.Equal("—", accounts.Rows.Single(r => r.Name == "Unused").IncomeToDateText);
        // The group header still totals its accounts.
        Assert.Contains("1,500", accounts.Groups.Single(g => g.TypeText == "Checking").IncomeToDateText,
            StringComparison.Ordinal);
    });

    [Fact]
    public Task Account_totals_count_every_occurrence_of_a_repeating_income() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var clock = host.Get<IClock>();
        var account = await host.Get<IAccountRepository>().AddAsync(
            new Account { Name = "Payroll account", Type = AccountType.Checking }, CancellationToken.None);

        await host.Get<IEntryRepository>().AddAsync(
            new Entry
            {
                Date = clock.Today.AddMonths(-3),
                Amount = 1000m,
                Kind = EntryKind.Income,
                CategoryId = DefaultCategories.Salary,
                CurrencyCode = "USD",
                AccountId = account.Id,
                Frequency = RecurrenceFrequency.Monthly,
                DayOfMonth = 1,
            },
            CancellationToken.None);

        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();

        // Three monthly occurrences have passed since the series began.
        var row = accounts.Rows.Single(r => r.Name == "Payroll account");
        Assert.Equal(3000m, row.IncomeToDate);
    });

    [Fact]
    public Task Deleting_an_unused_account_just_deletes_it() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var accounts = host.Get<AccountsViewModel>();
        await AddCheckingAsync(host, "Spare");
        await accounts.ReloadAsync();

        var plan = await accounts.PrepareDeleteAsync(accounts.Rows.Single());

        Assert.False(plan.IsUsed);
        Assert.False(plan.NeedsReassignment);
        Assert.Contains("nothing else changes", plan.Message, StringComparison.Ordinal);
        Assert.True(await accounts.DeleteAsync(plan, null));
        Assert.Empty(accounts.Rows);
    });

    [Fact]
    public Task Deleting_an_account_in_use_moves_its_transactions_first() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var oldAccount = await AddCheckingAsync(host, "Old Checking");
        var newAccount = await AddCheckingAsync(host, "New Checking");
        var entries = host.Get<IEntryRepository>();
        var today = host.Get<IClock>().Today;
        await entries.AddAsync(
            new Entry
            {
                Date = today, Amount = 1200m, Kind = EntryKind.Income,
                CategoryId = DefaultCategories.Salary, CurrencyCode = "USD", AccountId = oldAccount.Id,
            },
            CancellationToken.None);

        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();
        var plan = await accounts.PrepareDeleteAsync(accounts.Rows.Single(r => r.Name == "Old Checking"));

        Assert.True(plan.IsUsed);
        Assert.Equal(1, plan.UsageCount);
        Assert.True(plan.NeedsReassignment);
        Assert.Equal(["New Checking"], plan.Replacements.Select(r => r.Label));

        Assert.True(await accounts.DeleteAsync(plan, newAccount.Id));

        // The transaction moved rather than losing its account.
        var moved = (await entries.GetAsync(new EntryFilter(), CancellationToken.None)).Single();
        Assert.Equal(newAccount.Id, moved.AccountId);
        Assert.Single(accounts.Rows);
        Assert.Contains("Moved 1 transaction", accounts.StatusText!, StringComparison.Ordinal);
    });

    [Fact]
    public Task An_account_in_use_with_nowhere_to_move_to_cannot_be_deleted() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var only = await AddCheckingAsync(host, "Only Checking");
        var today = host.Get<IClock>().Today;
        await host.Get<IEntryRepository>().AddAsync(
            new Entry
            {
                Date = today, Amount = 800m, Kind = EntryKind.Income,
                CategoryId = DefaultCategories.Salary, CurrencyCode = "USD", AccountId = only.Id,
            },
            CancellationToken.None);

        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();
        var plan = await accounts.PrepareDeleteAsync(accounts.Rows.Single());

        Assert.True(plan.IsBlocked);
        Assert.Contains("Add one first", plan.Message, StringComparison.Ordinal);
        Assert.False(await accounts.DeleteAsync(plan, null));
        Assert.Single(accounts.Rows);
    });

    [Fact]
    public Task Replacements_stay_on_the_same_side_of_the_ledger() => Run(async () =>
    {
        using var host = new TestHost();
        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();

        var checking = accounts.Rows.Single(r => r.Name == "Demo Checking");
        var plan = await accounts.PrepareDeleteAsync(checking);

        // Income accounts can only be replaced by income accounts.
        Assert.Equal(
            new[] { "Demo Savings" },
            plan.Replacements.Select(r => r.Label));
    });

    [Fact]
    public Task Accounts_section_groups_the_seeded_accounts_by_type() => Run(async () =>
    {
        using var host = new TestHost();
        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();

        Assert.Equal(6, accounts.Rows.Count);
        Assert.Contains(accounts.Groups, g => g.TypeText == "Credit");
        Assert.Contains(accounts.Groups, g => g.TypeText == "Other expense");
        Assert.Contains(accounts.Groups, g => g.TypeText == "Mortgage");
        var card = accounts.Rows.Single(r => r.Name == "Demo Visa");
        Assert.Equal("Credit", card.TypeText);
        Assert.Equal("••••1111", card.DigitsText);
    });

    [Fact]
    public Task Adding_an_account_through_the_editor_shows_up_in_its_type_group() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();
        Assert.Empty(accounts.Rows);

        var editor = accounts.CreateEditor(null);
        editor.Name = "Rainy Day";
        editor.SelectedType = editor.Types.Single(t => t.Label == "Savings");
        Assert.True(await accounts.SaveEditorAsync(editor));

        var row = Assert.Single(accounts.Rows);
        Assert.Equal("Rainy Day", row.Name);
        Assert.Equal("Savings", Assert.Single(accounts.Groups).TypeText);
    });

    [Fact]
    public Task Account_editor_offers_every_type_and_rejects_duplicates() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();

        var first = accounts.CreateEditor(null);
        Assert.Equal(
            new[] { "Credit", "Checking", "Savings", "Investment", "Mortgage", "Other income", "Other expense" },
            first.Types.Select(t => t.Label));
        first.Name = "Everyday Checking";
        Assert.True(await accounts.SaveEditorAsync(first));

        var duplicate = accounts.CreateEditor(null);
        duplicate.Name = "everyday checking";
        Assert.False(await accounts.SaveEditorAsync(duplicate));
        Assert.NotNull(duplicate.ErrorText);

        var blank = accounts.CreateEditor(null);
        blank.Name = "   ";
        Assert.False(await accounts.SaveEditorAsync(blank));

        var badDigits = accounts.CreateEditor(null);
        badDigits.Name = "Card";
        badDigits.Last4 = "abcd";
        Assert.False(await accounts.SaveEditorAsync(badDigits));

        Assert.Single(accounts.Rows);
    });

    [Fact]
    public Task Editing_and_deleting_an_account_updates_the_list() => Run(async () =>
    {
        using var host = new TestHost(seedSampleData: false);
        var accounts = host.Get<AccountsViewModel>();
        await accounts.ReloadAsync();

        var editor = accounts.CreateEditor(null);
        editor.Name = "Brokerage";
        editor.SelectedType = editor.Types.Single(t => t.Label == "Investment");
        await accounts.SaveEditorAsync(editor);

        var row = Assert.Single(accounts.Rows);
        var edit = accounts.CreateEditor(row);
        edit.Name = "Retirement Brokerage";
        edit.Last4 = "9001";
        Assert.True(await accounts.SaveEditorAsync(edit));

        var updated = Assert.Single(accounts.Rows);
        Assert.Equal("Retirement Brokerage", updated.Name);
        Assert.Equal("••••9001", updated.DigitsText);

        var plan = await accounts.PrepareDeleteAsync(updated);
        Assert.False(plan.IsUsed);
        Assert.True(await accounts.DeleteAsync(plan, null));
        Assert.Empty(accounts.Rows);
        Assert.Equal(PageState.Empty, accounts.State);
    });

    [Fact]
    public Task Settings_no_longer_edits_currency_or_budget() => Run(async () =>
    {
        using var host = new TestHost();
        var settings = host.Get<SettingsViewModel>();
        await settings.ReloadAsync();

        var properties = settings.GetType().GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("SelectedCurrency", properties);
        Assert.DoesNotContain("MonthlyBudgetText", properties);
        Assert.DoesNotContain("Currencies", properties);
        // Theme, import mode and the data actions stay.
        Assert.Contains("SelectedTheme", properties);
        Assert.Contains("ImportModes", properties);
    });

    [Fact]
    public Task About_shows_the_description_version_and_github_link() => Run(async () =>
    {
        using var host = new TestHost();
        var about = host.Get<AboutViewModel>();
        await about.ReloadAsync();

        Assert.Equal("Money Calendar", about.AppName);
        // Not a literal version: the release workflow stamps the real one into the project
        // before it runs the tests, so anything pinned here fails every release.
        Assert.Matches(@"^Version \d+\.\d+\.\d+", about.VersionText);
        Assert.Contains("income and expenses", about.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://github.com/LoxSmoke/money-calendar", about.RepoUrl);
        Assert.Equal("https://github.com/LoxSmoke/money-calendar/issues", about.IssuesUrl);
    });

    [Fact]
    public Task About_lists_system_info_and_the_MIT_license() => Run(async () =>
    {
        using var host = new TestHost();
        var about = host.Get<AboutViewModel>();
        await about.ReloadAsync();

        var labels = about.SystemInfo.Select(r => r.Label).ToList();
        Assert.Contains("Operating system", labels);
        Assert.Contains("Runtime", labels);
        Assert.Contains("UI toolkit", labels);
        Assert.Contains("Memory", labels);
        Assert.Contains("Database file", labels);
        Assert.All(about.SystemInfo, row => Assert.False(string.IsNullOrWhiteSpace(row.Value)));

        Assert.Equal("MIT License", about.LicenseName);
        Assert.Contains("MIT License", about.LicenseText, StringComparison.Ordinal);
        Assert.Contains("WITHOUT WARRANTY OF ANY KIND", about.LicenseText, StringComparison.Ordinal);
    });

    [Fact]
    public Task About_copy_info_produces_one_line_per_fact() => Run(async () =>
    {
        using var host = new TestHost();
        var about = host.Get<AboutViewModel>();
        await about.ReloadAsync();

        var diagnostics = about.BuildDiagnostics();
        var lines = diagnostics.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // A header line, then every fact except the version (which the header already carries).
        Assert.StartsWith("Money Calendar Version", lines[0], StringComparison.Ordinal);
        Assert.Equal(about.SystemInfo.Count, lines.Length);
        Assert.Contains(lines, l => l.StartsWith("Operating system:", StringComparison.Ordinal));
    });

}
