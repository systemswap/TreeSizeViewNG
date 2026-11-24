using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TreeSizeViewNG.Services;
using TreeSizeViewNG.ViewModels;

namespace TreeSizeViewNG;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    /// <summary>Zeigt einen unerwarteten Fehler in der Statusleiste an (Details stehen im Log).</summary>
    public void ShowError(Exception exception) =>
        ViewModel.ReportError(exception.Message);

    // "async void" ist bei Klick-Handlern nötig. Jede Ausnahme darin würde das Programm sofort
    // beenden, daher fängt jeder Handler seine Fehler selbst ab und protokolliert sie.

    private async void ChooseFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var options = new FolderPickerOpenOptions
            {
                Title = "Ordner zum Analysieren auswählen",
                AllowMultiple = false
            };

            var current = ViewModel.FolderPath.Trim();
            if (current.Length > 0 && Directory.Exists(current))
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(current);

            var result = await StorageProvider.OpenFolderPickerAsync(options);
            if (result.Count != 1)
                return;

            // TryGetLocalPath statt Path.LocalPath: liefert auch für Laufwerke (C:\) und
            // Sonderorte einen sauberen Pfad bzw. null, statt eine Ausnahme zu werfen.
            var path = result[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                ViewModel.ReportError("Der gewählte Ort ist kein lokaler Ordner.");
                return;
            }

            ViewModel.FolderPath = path;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Ordner auswählen", ex);
            ViewModel.ReportError($"Ordnerauswahl fehlgeschlagen: {ex.Message}");
        }
    }

    private async void StartScan_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.StartScanAsync();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Scan", ex);
            ViewModel.ReportError($"Scan fehlgeschlagen: {ex.Message}");
        }
    }

    private void StopScan_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.StopScan();

    private async void ExportCsv_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!ViewModel.CanExport)
                return;

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export speichern",
                SuggestedFileName = ViewModel.SuggestedExportFileName,
                DefaultExtension = "csv",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV (Excel, Semikolon-getrennt)") { Patterns = ["*.csv"] }
                ]
            });

            if (file is null)
                return;

            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
                stream.SetLength(0);
            await ViewModel.ExportCsvAsync(stream, file.TryGetLocalPath() ?? file.Name);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("CSV-Export", ex);
            ViewModel.ReportError($"Export fehlgeschlagen: {ex.Message}");
        }
    }

    private void ExpandAll_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.ExpandAll();

    private void CollapseAll_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.CollapseAll();

    private void ExpandOneLevel_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.ExpandOneLevel();

    private void CollapseOneLevel_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.CollapseOneLevel();

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
