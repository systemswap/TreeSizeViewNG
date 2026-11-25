using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using TreeSizeViewNG.Localization;
using TreeSizeViewNG.Models;
using TreeSizeViewNG.Services;

namespace TreeSizeViewNG.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DirectoryScanner _scanner = new();
    private CancellationTokenSource? _scanCts;
    private FolderNode? _root;
    private string _folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _searchText = string.Empty;
    private decimal? _minimumSizeMb = 0m;
    private string _statusText = Strings.Ready;
    private bool _isScanning;
    private bool _isExporting;
    private SortableTreeSource _source;
    private RowList _rows = new([]);
    private bool _isWorking;
    private bool _filterActive;
    private int _searchVersion;
    private SortColumn? _sortKey;
    private ListSortDirection _sortDirection = ListSortDirection.Descending;
    private string _sortInfo = string.Empty;
    private readonly ProgressCounter _progress = new();
    private DispatcherTimer? _progressTimer;
    private DispatcherTimer? _liveTimer;
    private FolderNode? _liveRoot;
    private FolderNode? _selectedNode;
    private bool _suppressSelection;
    private double _progressPercent;
    private string _progressText = string.Empty;
    private bool _disposed;
    private int _currentExpansionDepth;
    private int _maximumExpansionDepth;
    private bool _includeParentLevels;

    public MainWindowViewModel()
    {
        _source = CreateSource();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Bittet die Ansicht, die Zeile mit diesem Index in den sichtbaren Bereich zu holen.</summary>
    public event Action<int>? ScrollToRowRequested;

    public string FolderPath
    {
        get => _folderPath;
        set => Set(ref _folderPath, value ?? string.Empty);
    }

    public decimal? MinimumSizeMb
    {
        get => _minimumSizeMb;
        set => Set(ref _minimumSizeMb, value is null ? 0m : Math.Max(0m, value.Value));
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (Set(ref _statusText, value))
                OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    /// <summary>Statuszeile: Status plus ggf. Hinweis auf die aktuelle Sortierung.</summary>
    public string StatusDisplay => StatusText + _sortInfo;

    public bool IsScanning
    {
        get => _isScanning;
        private set => Set(ref _isScanning, value);
    }

    /// <summary>Fortschrittsanzeige: beim Scannen und während der Baum (z. B. sortiert) neu aufgebaut wird.</summary>
    public bool IsBusy => IsScanning || _isWorking;

    /// <summary>Fortschritt in Prozent (Kopieren, Sortieren, Aufklappen); beim Scan unbestimmt.</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => Set(ref _progressPercent, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => Set(ref _progressText, value);
    }

    public bool IsProgressIndeterminate => IsScanning;

    public bool CanStart => !IsScanning;
    public bool CanStop => IsScanning;
    public bool CanExport => !_disposed && !IsScanning && !_isWorking && !_isExporting && _root is not null;

    /// <summary>
    /// Aus: Ebene 1 im Export ist der ausgewählte Ordner (bzw. der gescannte Ordner).
    /// An: Ebene 1 ist das Laufwerk (z. B. "C:"), danach alle übergeordneten Ordner.
    /// </summary>
    public bool IncludeParentLevels
    {
        get => _includeParentLevels;
        set => Set(ref _includeParentLevels, value);
    }

    /// <summary>Kurzinfo über der Export-Schaltfläche, ab welchem Ordner exportiert wird.</summary>
    public string ExportScopeText
    {
        get
        {
            var node = GetExportNode();
            if (node is null)
                return Strings.ExportNotYet;
            return ReferenceEquals(node, _root)
                ? Strings.FormatExportFromWholeScan(node.Name)
                : Strings.FormatExportFrom(node.Name);
        }
    }

    public string ExportScopePath => GetExportNode()?.FullPath ?? string.Empty;

    public string SuggestedExportFileName
    {
        get
        {
            var name = GetExportNode()?.Name ?? "Export";
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return $"TreeSize_{name}_{DateTime.Now:yyyy-MM-dd_HHmm}.csv";
        }
    }
    public bool CanExpandOneLevel => CanInteract && _currentExpansionDepth < _maximumExpansionDepth;
    public bool CanCollapseOneLevel => CanInteract && _currentExpansionDepth > 0;

    /// <summary>Baum darf verändert werden (nicht während Scan, Sortieren oder Suchen).</summary>
    private bool CanInteract => !_disposed && !IsScanning && !_isWorking && _root is not null;
    public string ExpansionDepthText => _root is null
        ? Strings.LevelNone
        : Strings.FormatLevel(_currentExpansionDepth, _maximumExpansionDepth);

    // Die beiden Zahlen in "Ebene 12 / 23" sind anklickbar
    public string CurrentLevelText => _root is null ? "–" : _currentExpansionDepth.ToString();
    public string MaximumLevelText => _root is null ? "–" : _maximumExpansionDepth.ToString();
    public bool CanUseLevelButtons => CanInteract;

    /// <summary>Klick auf die aktuelle Ebene: alle Ordner wieder einheitlich auf diese Ebene setzen.</summary>
    public void ReapplyCurrentLevel() => SetExpansionLevel(_currentExpansionDepth);

    /// <summary>
    /// Klick auf die tiefste Ebene: klappt den Weg zu einem Eintrag der tiefsten Ebene auf
    /// (bevorzugt eine Datei), wählt ihn aus und holt ihn in den sichtbaren Bereich.
    /// </summary>
    public void JumpToDeepest()
    {
        var root = _root;
        if (!CanInteract || root is null)
            return;

        FolderNode deepest = root;
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (_filterActive && !node.MatchesFilter)
                continue;
            if (node.Depth > deepest.Depth || (node.Depth == deepest.Depth && deepest.IsFolder && !node.IsFolder))
                deepest = node;
            foreach (var child in node.Children)
                stack.Push(child);
        }

        for (var p = deepest.Parent; p is not null; p = p.Parent)
            p.IsExpanded = true;
        _selectedNode = deepest;
        RefreshRows();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
                _ = ApplyFilterDebouncedAsync();
        }
    }

    public SortableTreeSource Source
    {
        get => _source;
        private set
        {
            if (ReferenceEquals(_source, value))
                return;

            var previous = _source;
            _source = value;
            OnPropertyChanged();
            previous.Dispose();
        }
    }

    public async Task StartScanAsync()
    {
        if (_disposed || IsScanning)
            return;

        var selectedPath = FolderPath.Trim();
        if (selectedPath.Length == 0 || !Directory.Exists(selectedPath))
        {
            StatusText = Strings.PleaseChooseValidFolder;
            return;
        }

        var scanCts = new CancellationTokenSource();
        _scanCts = scanCts;
        // Neuer Scan: alte Ergebnisse weg, Live-Anzeige der obersten Ordner starten
        _root = null;
        _selectedNode = null;
        _filterActive = false;
        _sortKey = null;
        SetSortInfo(string.Empty);
        Volatile.Write(ref _liveRoot, null);
        Source = CreateSource();
        SetRows([]);
        IsScanning = true;
        NotifyButtonStates();
        StatusText = Strings.ScanStarting;
        _liveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => ShowLiveRows());
        _liveTimer.Start();

        var progress = new Progress<ScanProgress>(p =>
        {
            StatusText = Strings.FormatScanProgress(
                p.Files, p.Folders, SizeFormatter.Format(p.Bytes), p.SkippedEntries, p.CurrentPath);
        });

        try
        {
            var root = await _scanner.ScanAsync(
                selectedPath,
                ConvertMegabytesToBytes(MinimumSizeMb),
                progress,
                scanCts.Token,
                liveRoot => Volatile.Write(ref _liveRoot, liveRoot));

            _liveTimer?.Stop();
            if (_disposed)
                return;

            _root = root;
            _maximumExpansionDepth = root.GetMaxDepth();
            _currentExpansionDepth = _maximumExpansionDepth;
            Source = CreateSource(); // jetzt ggf. mit den Spalten "Nur online" und "Gesamt"
            IsScanning = false;
            if (SearchText.Trim().Length > 0)
                await ApplySearchAsync();
            else
                SetExpansionLevel(_currentExpansionDepth);
            StatusText = Strings.FormatScanDone(root.FileCount, root.FolderCount, root.SizeText) +
                         (root.OnlineOnlyFileCount > 0
                             ? Strings.FormatOnlineOnlySummary(root.OnlineOnlyFileCount, SizeFormatter.Format(root.OnlineOnlyBytes))
                             : string.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusText = Strings.ScanCancelled;
        }
        catch (Exception ex)
        {
            StatusText = Strings.FormatError(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_scanCts, scanCts))
                _scanCts = null;

            scanCts.Dispose();
            _liveTimer?.Stop();
            IsScanning = false;
            NotifyButtonStates();
        }
    }

    public void StopScan() => _scanCts?.Cancel();

    public void ReportError(string message) => StatusText = Strings.FormatErrorWithLog(message, ErrorLog.FilePath);

    /// <summary>
    /// Exportiert ab dem im Baum ausgewählten Ordner (ohne Auswahl: der ganze Scan), immer
    /// vollständig – unabhängig von der aktuell aufgeklappten Ebene. Ist eine Datei ausgewählt,
    /// wird ihr Ordner exportiert. Ein Suchtext filtert auch den Export.
    /// </summary>
    public async Task ExportCsvAsync(Stream destination, string displayPath)
    {
        var root = GetExportNode();
        if (!CanExport || root is null)
            return;

        _isExporting = true;
        NotifyButtonStates();
        StatusText = Strings.ExportRunning;

        try
        {
            var search = SearchText.Trim();
            var exportRoot = search.Length == 0 ? root : await Task.Run(() => root.CreateFilteredCopy(search));
            var result = await CsvExporter.ExportAsync(exportRoot, destination, IncludeParentLevels);
            StatusText = Strings.FormatExportSaved(
                root.Name, search.Length == 0 ? null : search, result.Rows, result.LevelColumns, displayPath);
        }
        catch (Exception ex)
        {
            StatusText = Strings.FormatExportFailed(ex.Message);
        }
        finally
        {
            _isExporting = false;
            NotifyButtonStates();
        }
    }

    // Ebenen-Steuerung: Der Baum enthält immer alle Ebenen, einzelne Ordner lassen sich jederzeit
    // auf- und zuklappen. Diese Befehle setzen alle Ordner einheitlich auf eine Ebene: alles bis
    // dorthin wird geöffnet, tiefer (auch einzeln) aufgeklappte Ordner werden darauf zurückgesetzt.

    public void ExpandAll() => SetExpansionLevel(_maximumExpansionDepth);

    public void CollapseAll() => SetExpansionLevel(0);

    public void ExpandOneLevel()
    {
        if (CanExpandOneLevel)
            SetExpansionLevel(_currentExpansionDepth + 1);
    }

    public void CollapseOneLevel()
    {
        if (CanCollapseOneLevel)
            SetExpansionLevel(_currentExpansionDepth - 1);
    }

    private void SetExpansionLevel(int level)
    {
        var root = _root;
        if (!CanInteract || root is null)
            return;

        _currentExpansionDepth = Math.Clamp(level, 0, _maximumExpansionDepth);
        SetExpandedFlags(root, _currentExpansionDepth);
        RefreshRows();
        NotifyButtonStates();
    }

    /// <summary>Alle Ordner mit Tiefe kleiner als die Ebene auf, alle anderen zu.</summary>
    private static void SetExpandedFlags(FolderNode root, int level)
    {
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            node.IsExpanded = node.Depth < level;
            foreach (var child in node.Children)
            {
                if (child.IsFolder)
                    stack.Push(child);
            }
        }
    }

    /// <summary>Einen einzelnen Ordner auf- oder zuklappen (Pfeil vor dem Namen, Doppelklick).</summary>
    public void Toggle(FolderNode node)
    {
        if (!CanInteract || !node.HasChildren)
            return;

        var index = _rows.IndexOf(node);
        if (index < 0)
            return;

        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            var end = index + 1;
            while (end < _rows.Count && _rows[end].Depth > node.Depth)
                end++;
            // lag die Auswahl im zugeklappten Teil, geht sie auf den Ordner selbst über
            var selectedIndex = _selectedNode is null ? -1 : _rows.IndexOf(_selectedNode, index + 1, end - index - 1);
            _rows.RemoveRange(index + 1, end - index - 1);
            if (selectedIndex >= 0)
                Select(node, index);
        }
        else
        {
            node.IsExpanded = true;
            var rows = new List<FolderNode>();
            AppendVisible(node, rows, _filterActive, includeSelf: false);
            _rows.InsertRange(index + 1, rows);
        }
    }

    public void ToggleSelected()
    {
        if (_selectedNode is { } node)
            Toggle(node);
    }

    /// <summary>Sichtbare Zeilen eines Teilbaums in Anzeigereihenfolge (aufgeklappte Ordner, ggf. gefiltert).</summary>
    private static void AppendVisible(FolderNode start, List<FolderNode> rows, bool filtered, bool includeSelf)
    {
        var stack = new Stack<FolderNode>();
        if (includeSelf)
        {
            if (filtered && !start.MatchesFilter)
                return;
            stack.Push(start);
        }
        else
        {
            PushChildren(start);
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            rows.Add(node);
            if (node.IsFolder && node.IsExpanded)
                PushChildren(node);
        }

        void PushChildren(FolderNode node)
        {
            var children = node.Children;
            for (var index = children.Count - 1; index >= 0; index--)
            {
                if (!filtered || children[index].MatchesFilter)
                    stack.Push(children[index]);
            }
        }
    }

    /// <summary>Berechnet die sichtbaren Zeilen neu und zeigt sie an.</summary>
    private void RefreshRows()
    {
        var rows = new List<FolderNode>();
        if (_root is { } root)
            AppendVisible(root, rows, _filterActive, includeSelf: true);
        SetRows(rows);
    }

    private void SetRows(List<FolderNode> rows)
    {
        _suppressSelection = true;
        try
        {
            _rows = new RowList(rows);
            Source.Items = _rows;

            // Auswahl behalten; ist der Eintrag jetzt zugeklappt, den nächsten sichtbaren Ordner darüber wählen
            var target = VisibleAncestorOrSelf(_selectedNode);
            var index = target is null ? -1 : _rows.IndexOf(target);
            if (index >= 0 && Source.RowSelection is { } selection)
            {
                _selectedNode = target;
                selection.SelectedIndex = index;
                ScrollToRowRequested?.Invoke(index);
            }
            else
            {
                _selectedNode = null;
            }
        }
        finally
        {
            _suppressSelection = false;
        }

        NotifyExportScope();
    }

    /// <summary>Der Eintrag selbst, wenn alle Ordner über ihm aufgeklappt sind, sonst der oberste zugeklappte Ordner darüber.</summary>
    private static FolderNode? VisibleAncestorOrSelf(FolderNode? node)
    {
        var visible = node;
        for (var p = node?.Parent; p is not null; p = p.Parent)
        {
            if (!p.IsExpanded)
                visible = p;
        }
        return visible;
    }

    private void Select(FolderNode node, int index)
    {
        if (Source.RowSelection is not { } selection)
            return;

        _suppressSelection = true;
        try
        {
            _selectedNode = node;
            selection.SelectedIndex = index;
        }
        finally
        {
            _suppressSelection = false;
        }

        NotifyExportScope();
    }

    /// <summary>Während des Scans: Stammordner und oberste Ebene mit den bisher gezählten Größen.</summary>
    private void ShowLiveRows()
    {
        var root = Volatile.Read(ref _liveRoot);
        if (!IsScanning || root is null)
            return;

        var children = root.Children.ToArray();
        Array.Sort(children, static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        var rootSize = Math.Max(1d, root.SizeBytes);
        root.Percent = 100;
        root.IsExpanded = true;
        foreach (var child in children)
            child.Percent = child.IsOnlineOnly ? 0 : child.SizeBytes * 100d / rootSize;

        var rows = new List<FolderNode>(children.Length + 1) { root };
        rows.AddRange(children);
        SetRows(rows);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scanCts?.Cancel();
        _source.Dispose();
    }

    /// <summary>Suche erst anwenden, wenn 300 ms nicht mehr getippt wurde.</summary>
    private async Task ApplyFilterDebouncedAsync()
    {
        var version = ++_searchVersion;
        await Task.Delay(300);
        while (_isWorking && version == _searchVersion)
            await Task.Delay(30);
        if (version == _searchVersion && !_disposed && !IsScanning)
            await ApplySearchAsync();
    }

    /// <summary>
    /// Markiert im Hintergrund, welche Einträge zur Suche passen (selbst oder über Unterordner),
    /// und zeigt die Treffer aufgeklappt. Ohne Suchtext gilt wieder die eingestellte Ebene.
    /// </summary>
    private async Task ApplySearchAsync()
    {
        var root = _root;
        if (_disposed || root is null || _isWorking)
            return;

        var search = SearchText.Trim();
        SetWorking(true);
        try
        {
            _progress.Start(Strings.PhaseFilter, root.NodeCount);
            var progress = _progress;
            await Task.Run(() => MarkFilter(root, search, progress));
            _filterActive = search.Length > 0;
            SetExpandedFlags(root, _filterActive ? int.MaxValue : _currentExpansionDepth);
            RefreshRows();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Suche", ex);
            ReportError(ex.Message);
        }
        finally
        {
            SetWorking(false);
        }
    }

    private static bool MarkFilter(FolderNode node, string search, ProgressCounter progress)
    {
        var any = false;
        foreach (var child in node.Children)
            any |= MarkFilter(child, search, progress);

        if (node.IsFolder)
            progress.Step(1 + node.Children.Count(c => !c.IsFolder));
        node.MatchesFilter = search.Length == 0 || any || node.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
        return node.MatchesFilter;
    }

    /// <summary>
    /// Sortiert die Kinder aller Ordner im Hintergrund (parallel, direkt in den Daten – keine Kopie)
    /// und baut danach nur die sichtbare Zeilenliste neu. Aufgeklappte Ordner bleiben aufgeklappt.
    /// </summary>
    private async Task SortAsync(SortColumn key, ListSortDirection direction)
    {
        var root = _root;
        if (root is null)
            return;

        SetWorking(true);
        SetSortInfo(Strings.FormatSorting(ColumnName(key)));
        try
        {
            var sw = Stopwatch.StartNew();
            _progress.Start(Strings.PhaseSort, root.NodeCount);
            var progress = _progress;
            await Task.Run(() => TreeSorter.Sort(root, key, direction, progress));
            if (_disposed)
                return;

            RefreshRows();
            SetSortInfo(Strings.FormatSorted(
                ColumnName(key), direction == ListSortDirection.Ascending, sw.Elapsed.TotalSeconds));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Sortieren", ex);
            ReportError(ex.Message);
        }
        finally
        {
            SetWorking(false);
        }
    }

    private void SetSortInfo(string value)
    {
        _sortInfo = value;
        OnPropertyChanged(nameof(StatusDisplay));
    }

    private void SetWorking(bool value)
    {
        if (_isWorking == value)
            return;

        _isWorking = value;
        if (value)
        {
            _progress.Start(string.Empty, 1);
            ProgressPercent = 0;
            ProgressText = string.Empty;
            _progressTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => UpdateProgressFromCounter());
            _progressTimer.Start();
        }
        else
        {
            _progressTimer?.Stop();
            ProgressText = string.Empty;
        }

        NotifyButtonStates();
    }

    private void UpdateProgressFromCounter()
    {
        var phase = _progress.Phase;
        if (phase.Length == 0)
            return;

        var fraction = _progress.Fraction;
        ProgressPercent = fraction * 100;
        ProgressText = Strings.FormatProgress(phase, fraction);
    }

    /// <summary>Klick auf eine Spaltenüberschrift (siehe <see cref="SortableTreeSource"/>).</summary>
    private bool OnSortRequested(IColumn column, ListSortDirection direction)
    {
        var index = -1;
        for (var i = 0; i < Source.Columns.Count; i++)
        {
            if (ReferenceEquals(Source.Columns[i], column))
            {
                index = i;
                break;
            }
        }
        if (index < 0 || index >= Source.ColumnKeys.Count || !CanInteract)
            return false;
        var key = Source.ColumnKeys[index];

        // Richtung selbst bestimmen (die vom Grid vorgeschlagene passt nach dem Neuaufbau nicht
        // immer): erneuter Klick auf dieselbe Spalte dreht um; eine neue Spalte beginnt bei
        // Zahlen absteigend (größte zuerst), bei Name/Typ aufsteigend (A–Z).
        direction = key == _sortKey
            ? (_sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending)
            : (TreeSorter.IsNumeric(key) ? ListSortDirection.Descending : ListSortDirection.Ascending);

        _sortKey = key;
        _sortDirection = direction;
        foreach (var other in Source.Columns)
            other.SortDirection = null;
        column.SortDirection = direction; // Pfeil sofort zeigen
        _ = SortAsync(key, direction);
        return true;
    }

    private string ColumnName(SortColumn? key) => key switch
    {
        SortColumn.Name => Strings.ColumnName,
        SortColumn.Type => Strings.ColumnType,
        SortColumn.Local => ShowsCloudColumns ? Strings.ColumnLocal : Strings.ColumnSize,
        SortColumn.Online => Strings.ColumnOnline,
        SortColumn.Total => Strings.ColumnTotal,
        SortColumn.Files => Strings.ColumnFiles,
        SortColumn.Folders => Strings.ColumnFolders,
        SortColumn.Share => Strings.ColumnShare,
        _ => Strings.ColumnDefault
    };

    /// <summary>Spalten "Nur online" und "Gesamt" nur, wenn der Scan Online-Dateien gefunden hat.</summary>
    private bool ShowsCloudColumns => _root is { OnlineOnlyFileCount: > 0 };

    /// <summary>Ordner, ab dem exportiert wird: Auswahl im Baum (Datei → ihr Ordner) oder der Scan.</summary>
    private FolderNode? GetExportNode()
    {
        var root = _root;
        if (root is null || _selectedNode is null)
            return root;

        return _selectedNode.IsFolder ? _selectedNode : _selectedNode.Parent ?? root;
    }

    private void NotifyExportScope()
    {
        OnPropertyChanged(nameof(ExportScopeText));
        OnPropertyChanged(nameof(ExportScopePath));
    }

    private SortableTreeSource CreateSource()
    {
        var cloud = ShowsCloudColumns;
        var source = new SortableTreeSource(OnSortRequested);
        if (source.RowSelection is { } selection)
        {
            selection.SelectionChanged += (_, _) =>
            {
                if (_suppressSelection)
                    return;
                _selectedNode = selection.SelectedItem;
                NotifyExportScope();
            };
        }
        var keys = new List<SortColumn>();

        void Add(SortColumn key, IColumn<FolderNode> column)
        {
            keys.Add(key);
            source.Columns.Add(column);
        }

        Add(SortColumn.Name,
            new TemplateColumn<FolderNode>(
                Strings.ColumnName,
                CreateNameCellTemplate(),
                null,
                new GridLength(1, GridUnitType.Star),
                new TemplateColumnOptions<FolderNode>
                {
                    IsTextSearchEnabled = true,
                    TextSearchValueSelector = node => node.Name,
                    CompareAscending = (left, right) => CompareNames(left, right),
                    CompareDescending = (left, right) => CompareNames(right, left)
                }));
        Add(SortColumn.Type, new TextColumn<FolderNode, string>(
            Strings.ColumnType,
            node => node.TypeText,
            new GridLength(cloud ? 150 : 130),
            new TextColumnOptions<FolderNode>
            {
                CompareAscending = (left, right) => string.Compare(left?.TypeText, right?.TypeText, StringComparison.CurrentCultureIgnoreCase),
                CompareDescending = (left, right) => string.Compare(right?.TypeText, left?.TypeText, StringComparison.CurrentCultureIgnoreCase)
            }));

        if (cloud)
        {
            // Drei Größen, sobald es Dateien gibt, die nur online liegen
            Add(SortColumn.Local, new TextColumn<FolderNode, string>(Strings.ColumnLocal, node => node.LocalText,
                new GridLength(125), CreateLongOptions(node => node.LocalBytes)));
            Add(SortColumn.Online, new TextColumn<FolderNode, string>(Strings.ColumnOnline, node => node.OnlineText,
                new GridLength(115), CreateLongOptions(node => node.OnlineBytes)));
            Add(SortColumn.Total, new TextColumn<FolderNode, string>(Strings.ColumnTotal, node => node.TotalText,
                new GridLength(115), CreateLongOptions(node => node.TotalBytes)));
        }
        else
        {
            Add(SortColumn.Local, new TextColumn<FolderNode, string>(Strings.ColumnSize, node => node.SizeText,
                new GridLength(145), CreateLongOptions(node => node.LocalBytes)));
        }

        Add(SortColumn.Files, new TextColumn<FolderNode, string>(Strings.ColumnFiles, node => node.FileCountText,
            new GridLength(cloud ? 95 : 110), CreateLongOptions(node => node.FileCount)));
        Add(SortColumn.Folders, new TextColumn<FolderNode, string>(Strings.ColumnFolders, node => node.FolderCountText,
            new GridLength(cloud ? 85 : 100), CreateLongOptions(node => node.FolderCount)));
        Add(SortColumn.Share, new TextColumn<FolderNode, string>(Strings.ColumnShare, node => node.PercentText,
            new GridLength(85), CreateDoubleOptions(node => node.Percent)));

        source.ColumnKeys = keys;
        return source;
    }

    private static readonly IValueConverter ChevronConverter =
        new FuncValueConverter<bool, MaterialIconKind>(expanded => expanded ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight);

    private IDataTemplate CreateNameCellTemplate() =>
        new FuncDataTemplate<FolderNode>((node, _) =>
        {
            if (node is null)
                return new TextBlock();

            var icon = new MaterialIcon
            {
                Kind = node.IconKind,
                Width = 19,
                Height = 19,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = node.IsFolder
                    ? Brush.Parse("#E5A50A")
                    : node.IsOnlineOnly ? Brush.Parse("#5B8DEF") : Brush.Parse("#64748B")
            };

            var name = new TextBlock
            {
                Text = node.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (node.IsOnlineOnly || node.IsOnlineOnlyFolder)
            {
                // nicht auf der Platte: blasser und kursiv
                name.FontStyle = FontStyle.Italic;
                name.Opacity = 0.7;
            }

            // Einrückung nach Tiefe und eigener Aufklapp-Pfeil (die Zeilenliste ist flach)
            var toggle = new Border { Width = 22, Height = 24, Background = Brushes.Transparent };
            if (node.HasChildren)
            {
                var chevron = new MaterialIcon { Width = 16, Height = 16, Foreground = Brush.Parse("#8A93A0") };
                chevron.Bind(MaterialIcon.KindProperty, new Binding(nameof(FolderNode.IsExpanded)) { Source = node, Converter = ChevronConverter });
                toggle.Child = chevron;
                toggle.Cursor = new Cursor(StandardCursorType.Hand);
                toggle.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    Toggle(node);
                };
            }

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(node.Depth * 18, 0, 0, 0)
            };
            panel.Children.Add(toggle);
            panel.Children.Add(icon);
            panel.Children.Add(name);
            return panel;
        });

    private static TextColumnOptions<FolderNode> CreateLongOptions(Func<FolderNode, long> selector) => new()
    {
        TextAlignment = TextAlignment.Right,
        CompareAscending = (left, right) => CompareValues(left, right, selector),
        CompareDescending = (left, right) => CompareValues(right, left, selector)
    };

    private static TextColumnOptions<FolderNode> CreateDoubleOptions(Func<FolderNode, double> selector) => new()
    {
        TextAlignment = TextAlignment.Right,
        CompareAscending = (left, right) => CompareValues(left, right, selector),
        CompareDescending = (left, right) => CompareValues(right, left, selector)
    };

    private static int CompareValues<T>(FolderNode? left, FolderNode? right, Func<FolderNode, T> selector)
        where T : IComparable<T>
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        return selector(left).CompareTo(selector(right));
    }

    private static int CompareNames(FolderNode? left, FolderNode? right) =>
        StringComparer.CurrentCultureIgnoreCase.Compare(left?.Name, right?.Name);

    private static long ConvertMegabytesToBytes(decimal? megabytes)
    {
        var value = Math.Max(0m, megabytes ?? 0m);
        var bytes = value * 1024m * 1024m;

        return bytes >= long.MaxValue
            ? long.MaxValue
            : decimal.ToInt64(decimal.Truncate(bytes));
    }

    private void NotifyButtonStates()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(CanExport));
        NotifyExportScope();
        OnPropertyChanged(nameof(CanExpandOneLevel));
        OnPropertyChanged(nameof(CanCollapseOneLevel));
        OnPropertyChanged(nameof(ExpansionDepthText));
        OnPropertyChanged(nameof(CurrentLevelText));
        OnPropertyChanged(nameof(MaximumLevelText));
        OnPropertyChanged(nameof(CanUseLevelButtons));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
