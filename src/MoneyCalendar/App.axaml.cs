using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MoneyCalendar.Bootstrap;
using MoneyCalendar.Services;
using MoneyCalendar.ViewModels;
using MoneyCalendar.Views;
using MoneyCalendar.Data;
using Serilog;

namespace MoneyCalendar;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        StartupTrace.Write("App.Initialize entered");
        AvaloniaXamlLoader.Load(this);
        StartupTrace.Write("App.Initialize completed");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless test sessions have no desktop lifetime: they build their own service graph
        // and windows, so the host must not start here.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupTrace.Write("Building app host");
            _host = AppHost.Build();
            _host.Start();
            Log.Information("Money Calendar starting");

            // Open (and create) the database before any view model touches a repository.
            StartupTrace.Write("Initializing database");
            _host.Services.GetRequiredService<DatabaseBootstrapper>()
                .InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            StartupTrace.Write("Database initialized");

            RequestedThemeVariant = _host.Services.GetRequiredService<ISettingsStore>().Current.Theme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = _host.Services.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                Log.Information("Money Calendar shutting down");
                // Stop/dispose off the UI thread with a bounded wait: blocking the dispatcher
                // on host disposal can deadlock when a service continuation needs it.
                var stopped = Task.Run(async () =>
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                    _host.Dispose();
                }).Wait(TimeSpan.FromSeconds(10));
                if (!stopped)
                    Log.Warning("Host did not stop within 10s; exiting anyway");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
