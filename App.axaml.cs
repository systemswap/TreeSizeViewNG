using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TreeSizeViewNG.Services;

namespace TreeSizeViewNG;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Fehler im UI-Thread (z. B. in Klick-Handlern oder beim Zeichnen) sollen das
        // Programm nicht beenden: protokollieren und in der Statusleiste melden.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            ErrorLog.Write("Fehler im UI-Thread", e.Exception);
            e.Handled = true;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
                window.ShowError(e.Exception);
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
