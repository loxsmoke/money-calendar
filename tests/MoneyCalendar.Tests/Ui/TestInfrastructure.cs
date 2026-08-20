using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.DependencyInjection;
using MoneyCalendar.App.Bootstrap;
using MoneyCalendar.App.Services;
using MoneyCalendar.Data;
using MoneyCalendarApp = MoneyCalendar.App.App;

namespace MoneyCalendar.Tests.Ui;

// Avalonia.Headless.XUnit 12.x targets xunit v3 while this project is on xunit 2, so UI tests
// drive a HeadlessUnitTestSession directly via a collection fixture.

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MoneyCalendarApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("Headless")]
public class HeadlessCollection : ICollectionFixture<HeadlessSessionFixture>;

/// <summary>The app's own service graph, pointed at a throwaway database.</summary>
public sealed class TestHost : IDisposable
{
    private readonly string _directory;

    public TestHost(bool seedSampleData = true)
    {
        _directory = Path.Combine(Path.GetTempPath(), "money-calendar-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        var services = new ServiceCollection();
        Settings = new InMemorySettingsStore();
        AppHost.ConfigureServices(services, Settings, Path.Combine(_directory, "test.db"), seedSampleData);
        Provider = services.BuildServiceProvider();

        Provider.GetRequiredService<DatabaseBootstrapper>()
            .InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public ServiceProvider Provider { get; }
    public InMemorySettingsStore Settings { get; }

    public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    public void Dispose()
    {
        Provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
