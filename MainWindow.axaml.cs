using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TreeSizeViewNG.Localization;
using TreeSizeViewNG.Services;
using TreeSizeViewNG.ViewModels;

namespace TreeSizeViewNG;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        // Versionsnummer in der Titelleiste (Haupt.Neben.Commit-Anzahl)
        Title = $"TreeSizeViewNG {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        // Gewählte Zeile nach Auf-/Zuklappen sichtbar halten (erst nach dem Layout der neuen Zeilen)
        viewModel.ScrollToRowRequested += index =>
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => CenterRow(index),
                Avalonia.Threading.DispatcherPriority.Background);
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
                Title = Strings.ChooseFolderDialogTitle,
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
                ViewModel.ReportError(Strings.NotALocalFolder);
                return;
            }

            ViewModel.FolderPath = path;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Ordner auswählen", ex);
            ViewModel.ReportError(Strings.FormatFolderPickFailed(ex.Message));
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
            ViewModel.ReportError(Strings.FormatScanFailed(ex.Message));
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
                Title = Strings.SaveExportDialogTitle,
                SuggestedFileName = ViewModel.SuggestedExportFileName,
                DefaultExtension = "csv",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType(Strings.CsvFileType) { Patterns = ["*.csv"] }
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
            ViewModel.ReportError(Strings.FormatExportFailed(ex.Message));
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

    /// <summary>Holt eine Zeile in die Mitte des sichtbaren Bereichs.</summary>
    private void CenterRow(int index)
    {
        Tree.RowsPresenter?.BringIntoView(index);
        if (Tree.Scroll is not ScrollViewer scroll || Tree.TryGetRow(index) is not { } row)
            return;

        var rowHeight = row.Bounds.Height;
        if (rowHeight <= 0)
            return;

        var wanted = index * rowHeight - (scroll.Viewport.Height - rowHeight) / 2;
        var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Avalonia.Vector(scroll.Offset.X, Math.Clamp(wanted, 0, max));
    }

    private void CurrentLevel_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.ReapplyCurrentLevel();

    private void MaximumLevel_OnClick(object? sender, RoutedEventArgs e) =>
        ViewModel.JumpToDeepest();

    // Doppelklick auf eine Ordnerzeile klappt sie auf bzw. zu
    private void Tree_OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) =>
        ViewModel.ToggleSelected();

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Dispose();
        base.OnClosed(e);
    }
}
