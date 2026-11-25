using System.Globalization;

namespace TreeSizeViewNG.Localization;

/// <summary>
/// Alle sichtbaren Texte auf Deutsch und Englisch.
///
/// Die Sprache wird einmal beim Start festgelegt: Ist die Anzeigesprache des Betriebssystems Deutsch,
/// ist die Oberfläche deutsch, sonst englisch. Erzwingen lässt sie sich mit der Umgebungsvariablen
/// TREESIZEVIEWNG_LANG=de bzw. =en (z. B. zum Testen).
///
/// In XAML: Text="{x:Static loc:Strings.Scan}", im Code: Strings.Scan bzw. Strings.Format…(…).
/// </summary>
public static class Strings
{
    public static bool IsGerman { get; } = DetectGerman();

    private static bool DetectGerman()
    {
        var forced = Environment.GetEnvironmentVariable("TREESIZEVIEWNG_LANG");
        if (!string.IsNullOrWhiteSpace(forced))
            return forced.Trim().StartsWith("de", StringComparison.OrdinalIgnoreCase);

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
    }

    private static string T(string de, string en) => IsGerman ? de : en;

    // ----- Kopfzeile -----
    public static string AppSubtitle => T("Ordnergrößen schnell und übersichtlich analysieren", "Analyse folder sizes quickly and clearly");

    public static string ThemeToggleTip => T("Zwischen hellem und dunklem Design wechseln", "Switch between light and dark theme");

    // ----- Eingaben -----
    public static string FolderToAnalyse => T("ZU ANALYSIERENDER ORDNER", "FOLDER TO ANALYSE");
    public static string ChooseFolder => T("Ordner auswählen", "Choose folder");
    public static string ChooseFolderDialogTitle => T("Ordner zum Analysieren auswählen", "Choose a folder to analyse");
    public static string Scan => T("Scannen", "Scan");
    public static string Stop => T("Stop", "Stop");
    public static string Search => T("SUCHE", "SEARCH");
    public static string SearchWatermark => T("Ordner- oder Dateiname filtern …", "Filter by folder or file name …");
    public static string MinimumSize => T("MINDESTGRÖSSE", "MINIMUM SIZE");
    public static string ExpandAll => T("Alles ausklappen", "Expand all");
    public static string ExpandAllTip => T("Alle Ordner im Baum öffnen", "Open all folders in the tree");
    public static string CollapseAll => T("Alles einklappen", "Collapse all");
    public static string CollapseAllTip => T("Alle Ordner im Baum schließen", "Close all folders in the tree");
    public static string MatchingFilesHint => T("Passende Dateien werden im Baum angezeigt", "Matching files are shown in the tree");

    // ----- Baum -----
    public static string CollapseLevelTip => T("Eine Ebene global einklappen", "Collapse one level everywhere");
    public static string ExpandLevelTip => T("Eine Ebene global ausklappen", "Expand one level everywhere");
    public static string TreeTitle => T("Ordner- und Dateistruktur", "Folder and file structure");
    public static string SortHint => T("Klick auf eine Spaltenüberschrift zum Sortieren", "Click a column header to sort");
    public static string LevelWord => T("Ebene", "Level");
    public static string CurrentLevelTip => T("Alle Ordner einheitlich auf diese Ebene setzen", "Set all folders to this level");
    public static string MaximumLevelTip => T("Zu einem Eintrag der tiefsten Ebene springen", "Jump to an entry on the deepest level");
    public static string LevelNone => T("Ebene –", "Level –");
    public static string FormatLevel(int current, int max) => T($"Ebene {current} / {max}", $"Level {current} / {max}");

    public static string ColumnName => T("Name", "Name");
    public static string ColumnType => T("Typ", "Type");
    public static string ColumnSize => T("Größe", "Size");
    public static string ColumnLocal => T("Lokal", "Local");
    public static string ColumnOnline => T("In Cloud", "In cloud");
    public static string ColumnTotal => T("Gesamt", "Total");
    public static string ColumnFiles => T("Dateien", "Files");
    public static string ColumnFolders => T("Ordner", "Folders");
    public static string ColumnShare => T("Anteil", "Share");
    public static string ColumnDefault => T("Standard", "default");

    public static string TypeFolder => T("Ordner", "Folder");
    public static string TypeFile => T("Datei", "File");
    public static string FormatTypeFile(string extension) => T($"{extension}-Datei", $"{extension} file");
    public static string FormatOnlineOnly(string type) => T($"{type} · in Cloud", $"{type} · in cloud");
    public static string TypeFolderOnlineOnly => T("Ordner · in Cloud", "Folder · in cloud");
    public static string FormatOnlineOnlySummary(long files, string size) => T(
        $" · zusätzlich {files:N0} Dateien in der Cloud ({size}, nicht lokal)",
        $" · plus {files:N0} files in the cloud ({size}, not local)");

    // ----- SOLIDWORKS PDM -----
    public static string FormatInVault(string type) => T($"{type} · im Tresor", $"{type} · in vault");
    public static string TypeFolderInVault => T("Ordner · im Tresor", "Folder · in vault");
    public static string FormatPdmTitle(string vault) => $"PDM {vault}";
    public static string PdmConnecting => T("Tresor wird abgefragt …", "Querying vault …");
    public static string FormatPdmReading(int files) => T($"{files:N0} Dateien aus dem Tresor gelesen", $"{files:N0} files read from the vault");
    public static string PdmWaitingForScan => T("fertig gelesen, warte auf den Scan …", "read, waiting for the scan …");
    public static string PdmNotLoggedIn => T("nicht am Tresor angemeldet (bitte im Explorer am PDM-Tresor anmelden und erneut scannen)", "not logged in to the vault (please log in to the PDM vault in Explorer and scan again)");
    public static string PdmMerging => T("wird in den Baum eingetragen …", "merging into the tree …");
    public static string FormatPdmFailed(string message) => T($"Abfrage fehlgeschlagen: {message}", $"query failed: {message}");
    public static string FormatPdmSummary(string vault, int files, int vaultOnly, string vaultOnlySize, int mismatches) => T(
        $" · PDM {vault}: {files:N0} Dateien im Tresor, davon {vaultOnly:N0} nur dort ({vaultOnlySize}){(mismatches > 0 ? $", {mismatches:N0} lokal abweichend" : "")}",
        $" · PDM {vault}: {files:N0} files in the vault, {vaultOnly:N0} only there ({vaultOnlySize}){(mismatches > 0 ? $", {mismatches:N0} differ locally" : "")}");
    public static string TotalColumnTip => T(
        "Jede Datei einmal: die lokale Größe, sonst die Größe in der Cloud bzw. im Tresor.\nBei PDM-Dateien, die lokal und im Tresor liegen, ist „Gesamt“ deshalb nicht „Lokal“ + „In Cloud“.",
        "Each file once: its local size, otherwise its size in the cloud or vault.\nFor PDM files that exist locally and in the vault, “Total” is therefore not “Local” + “In cloud”.");
    public static string CloudColumnTip => T(
        "Größe in der Cloud bzw. im PDM-Tresor. Orange: die lokale Kopie hat eine andere Größe als die Datei im Tresor.",
        "Size in the cloud or PDM vault. Orange: the local copy differs in size from the file in the vault.");

    // ----- Fortschritt -----
    public static string PhaseFilter => T("Suchen", "Searching");
    public static string PhaseSort => T("Sortieren", "Sorting");
    public static string FormatProgress(string phase, double fraction) => $"{phase} · {fraction:P0}";

    // ----- Export -----
    public static string IncludeParentLevels => T("Übergeordnete Ebenen mitexportieren", "Include parent levels");
    public static string IncludeParentLevelsTip => T(
        "Aus: Ebene 1 ist der im Baum ausgewählte Ordner (ohne Auswahl der gescannte Ordner).\nAn: Ebene 1 ist das Laufwerk (z. B. C:), danach alle Ordner, in denen er liegt.",
        "Off: level 1 is the folder selected in the tree (without a selection, the scanned folder).\nOn: level 1 is the drive (e.g. C:), followed by every folder it is in.");
    public static string ExportCsv => T("CSV exportieren", "Export CSV");
    public static string ExportCsvTip => T(
        "Als CSV für Excel exportieren: ab dem im Baum ausgewählten Ordner (ohne Auswahl der ganze Scan), immer mit allen Unterebenen. Eine Zeile pro Ordner/Datei, Größe in Bytes, jede Ebene als eigene Spalte. Ein aktiver Suchfilter wird berücksichtigt.",
        "Export as CSV for Excel: from the folder selected in the tree (without a selection, the whole scan), always with all sub-levels. One row per folder/file, size in bytes, each level in its own column. An active search filter is applied.");
    public static string ExportNotYet => T("Export: erst scannen", "Export: scan first");
    public static string FormatExportFrom(string name) => T($"Export ab: {name}", $"Export from: {name}");
    public static string FormatExportFromWholeScan(string name) => T($"Export ab: {name} (ganzer Scan)", $"Export from: {name} (whole scan)");
    public static string SaveExportDialogTitle => T("Export speichern", "Save export");
    public static string CsvFileType => T("CSV (Excel)", "CSV (Excel)");
    public static string ExportRunning => T("Export läuft …", "Exporting …");
    public static string FormatExportSaved(string name, string? filter, long rows, int levels, string path) => T(
        $"Export ab {name} gespeichert{(filter is null ? "" : $" (Filter: \"{filter}\")")} · {rows:N0} Zeilen · {levels} Ebenen · {path}",
        $"Export from {name} saved{(filter is null ? "" : $" (filter: \"{filter}\")")} · {rows:N0} rows · {levels} levels · {path}");

    // CSV-Kopfzeile
    public static string CsvType => T("Typ", "Type");
    public static string CsvSizeBytes => T("Lokal (Bytes)", "Local (bytes)");
    public static string CsvOnlineBytes => T("In Cloud (Bytes)", "In cloud (bytes)");
    public static string CsvTotalBytes => T("Gesamt (Bytes)", "Total (bytes)");
    public static string CsvSizeMb => T("Lokal (MB)", "Local (MB)");
    public static string CsvShare => T("Anteil (%)", "Share (%)");
    public static string CsvFiles => T("Dateien", "Files");
    public static string CsvSubfolders => T("Unterordner", "Subfolders");
    public static string CsvDepth => T("Tiefe", "Depth");
    public static string CsvExtension => T("Erweiterung", "Extension");
    public static string CsvName => T("Name", "Name");
    public static string FormatCsvLevel(int level) => T($"Ebene {level}", $"Level {level}");
    public static string CsvFullPath => T("Vollständiger Pfad", "Full path");

    // ----- Status -----
    public static string Ready => T("Bereit", "Ready");
    public static string PleaseChooseValidFolder => T("Bitte einen gültigen Ordner auswählen.", "Please choose a valid folder.");
    public static string ScanStarting => T("Scan wird gestartet …", "Starting scan …");
    public static string FormatScanProgress(long files, long folders, string size, long skipped, string path) => T(
        $"{files:N0} Dateien geprüft · {folders:N0} Ordner · {size} berücksichtigt{(skipped > 0 ? $" · {skipped:N0} übersprungen" : "")} · {path}",
        $"{files:N0} files checked · {folders:N0} folders · {size} counted{(skipped > 0 ? $" · {skipped:N0} skipped" : "")} · {path}");
    public static string FormatScanDone(long files, long folders, string size) => T(
        $"Fertig · {files:N0} passende Dateien · {folders:N0} Ordner · {size}",
        $"Done · {files:N0} matching files · {folders:N0} folders · {size}");
    public static string ScanCancelled => T("Scan abgebrochen.", "Scan cancelled.");
    public static string FormatError(string message) => T($"Fehler: {message}", $"Error: {message}");
    public static string FormatErrorWithLog(string message, string logPath) => T(
        $"Fehler: {message} · Details: {logPath}", $"Error: {message} · Details: {logPath}");
    public static string NotALocalFolder => T("Der gewählte Ort ist kein lokaler Ordner.", "The selected location is not a local folder.");
    public static string FormatFolderPickFailed(string message) => T($"Ordnerauswahl fehlgeschlagen: {message}", $"Choosing the folder failed: {message}");
    public static string FormatScanFailed(string message) => T($"Scan fehlgeschlagen: {message}", $"Scan failed: {message}");
    public static string FormatExportFailed(string message) => T($"Export fehlgeschlagen: {message}", $"Export failed: {message}");
    public static string FormatSorting(string column) => T($" · sortiere nach {column} …", $" · sorting by {column} …");
    public static string FormatSorted(string column, bool ascending, double seconds) => T(
        $" · sortiert nach {column} ({(ascending ? "aufsteigend" : "absteigend")}, {seconds:N1} s)",
        $" · sorted by {column} ({(ascending ? "ascending" : "descending")}, {seconds:N1} s)");

    // ----- Fehlerprotokoll -----
    public static string ErrorLogFileName => T("fehler.log", "error.log");
}
