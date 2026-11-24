using Avalonia;
using TreeSizeViewNG.Services;

namespace TreeSizeViewNG;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Letzte Rettung: Fehler, die nirgends abgefangen werden, wenigstens protokollieren.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ErrorLog.Write("Unbehandelter Fehler (Programm beendet)", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write("Unbeobachteter Task-Fehler", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Fehler beim Start", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
