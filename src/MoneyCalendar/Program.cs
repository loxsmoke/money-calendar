using Avalonia;
using MoneyCalendar.Services;

namespace MoneyCalendar;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupTrace.Write("Unhandled AppDomain exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            StartupTrace.Write("Unobserved task exception", e.Exception);
        StartupTrace.Write("Program.Main entered");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            StartupTrace.Write("Avalonia desktop lifetime returned");
        }
        catch (Exception ex)
        {
            StartupTrace.Write("Avalonia desktop lifetime failed", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
