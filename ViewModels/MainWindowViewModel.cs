using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons.Avalonia;
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
    private string _statusText = "Bereit";
    private bool _isScanning;
    private bool _isExporting;
    private SortableTreeSource _source;
    private bool _isRebuilding;
    private int _rebuildVersion;
    private int _searchVersion;
    private int? _sortColumnIndex;
    private ListSortDirection _sortDirection = ListSortDirection.Descending;
    private string _sortInfo = string.Empty;
    private bool _disposed;
    private int _currentExpansionDepth;
    private int _maximumExpansionDepth;
    private string? _selectedPath;
    private bool _includeParentLevels;

    public MainWindowViewModel()
    {
        _source = CreateSource(Array.Empty<FolderNode>());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
    public bool IsBusy => IsScanning || _isRebuilding;

    public bool CanStart => !IsScanning;
    public bool CanStop => IsScanning;
    public bool CanExport => !_disposed && !IsScanning && !_isExporting && _root is not null;

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
                return "Export: erst scannen";
            return ReferenceEquals(node, _root)
                ? $"Export ab: {node.Name} (ganzer Scan)"
                : $"Export ab: {node.Name}";
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
    public bool CanExpandOneLevel => !_disposed && _root is not null && _currentExpansionDepth < _maximumExpansionDepth;
    public bool CanCollapseOneLevel => !_disposed && _root is not null && _currentExpansionDepth > 0;
    public string ExpansionDepthText => _root is null
        ? "Ebene –"
        : $"Ebene {_currentExpansionDepth} / {_maximumExpansionDepth}";

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
            StatusText = "Bitte einen gültigen Ordner auswählen.";
            return;
        }

        var scanCts = new CancellationTokenSource();
        _scanCts = scanCts;
        IsScanning = true;
        NotifyButtonStates();
        StatusText = "Scan wird gestartet …";

        var progress = new Progress<ScanProgress>(p =>
        {
            var skippedText = p.SkippedEntries > 0 ? $" · {p.SkippedEntries:N0} übersprungen" : string.Empty;
            StatusText = $"{p.Files:N0} Dateien geprüft · {p.Folders:N0} Ordner · " +
                         $"{SizeFormatter.Format(p.Bytes)} berücksichtigt{skippedText} · {p.CurrentPath}";
        });

        try
        {
            var root = await _scanner.ScanAsync(
                selectedPath,
                ConvertMegabytesToBytes(MinimumSizeMb),
                progress,
                scanCts.Token);

            if (_disposed)
                return;

            _root = root;
            _selectedPath = null;
            _maximumExpansionDepth = root.GetMaxDepth();
            _currentExpansionDepth = _maximumExpansionDepth;
            ApplyFilter();
            StatusText = $"Fertig · {root.FileCount:N0} passende Dateien · " +
                         $"{root.FolderCount:N0} Ordner · {root.SizeText}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_scanCts, scanCts))
                _scanCts = null;

            scanCts.Dispose();
            IsScanning = false;
            NotifyButtonStates();
        }
    }

    public void StopScan() => _scanCts?.Cancel();

    public void ReportError(string message) => StatusText = $"Fehler: {message} · Details: {ErrorLog.FilePath}";

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
        StatusText = "Export läuft …";

        try
        {
            var search = SearchText.Trim();
            var exportRoot = search.Length == 0 ? root : root.CreateFilteredCopy(search, int.MaxValue);
            var result = await CsvExporter.ExportAsync(exportRoot, destination, IncludeParentLevels);
            var filterText = search.Length == 0 ? string.Empty : $" (Filter: \"{search}\")";
            StatusText = $"Export ab {root.Name} gespeichert{filterText} · {result.Rows:N0} Zeilen · " +
                         $"{result.LevelColumns} Ebenen · {displayPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _isExporting = false;
            NotifyButtonStates();
        }
    }

    public void ExpandAll()
    {
        if (_disposed || _root is null)
            return;

        _currentExpansionDepth = _maximumExpansionDepth;
        ApplyFilter();
    }

    public void CollapseAll()
    {
        if (_disposed || _root is null)
            return;

        _currentExpansionDepth = 0;
        ApplyFilter();
    }

    public void ExpandOneLevel()
    {
        if (!CanExpandOneLevel)
            return;

        _currentExpansionDepth++;
        ApplyFilter();
    }

    public void CollapseOneLevel()
    {
        if (!CanCollapseOneLevel)
            return;

        _currentExpansionDepth--;
        ApplyFilter();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scanCts?.Cancel();
        _source.Dispose();
    }

    private void ApplyFilter() => _ = RebuildViewAsync(sortRequested: false);

    /// <summary>Suche erst anwenden, wenn 300 ms nicht mehr getippt wurde.</summary>
    private async Task ApplyFilterDebouncedAsync()
    {
        var version = ++_searchVersion;
        await Task.Delay(300);
        if (version == _searchVersion && !_disposed)
            await RebuildViewAsync(sortRequested: false);
    }

    /// <summary>
    /// Baut die Anzeige neu auf. Kopieren, Filtern und Sortieren der (evtl. Millionen) Knoten
    /// laufen im Hintergrund; im UI-Thread wird nur noch die fertige Quelle gesetzt.
    /// Wird währenddessen erneut neu aufgebaut, gewinnt der jüngste Aufruf.
    /// </summary>
    private async Task RebuildViewAsync(bool sortRequested)
    {
        if (_disposed)
            return;

        var version = ++_rebuildVersion;
        var root = _root;
        if (root is null)
        {
            Source = CreateSource(Array.Empty<FolderNode>());
            NotifyButtonStates();
            return;
        }

        var search = SearchText.Trim();
        var depth = _currentExpansionDepth;
        var sortColumn = _sortColumnIndex;
        var sortDirection = _sortDirection;
        SetRebuilding(true);
        if (sortRequested)
            SetSortInfo($" · sortiere nach {ColumnName(sortColumn)} …");

        try
        {
            var sw = Stopwatch.StartNew();
            var displayedRoot = await Task.Run(() => BuildDisplayTree(root, search, depth, sortColumn, sortDirection));
            if (version != _rebuildVersion || _disposed)
                return; // inzwischen überholt

            Source = displayedRoot is null
                ? CreateSource(Array.Empty<FolderNode>())
                : CreateSource(new[] { displayedRoot });

            Source.ExpandAll();
            if (sortColumn is { } index && index < Source.Columns.Count)
                Source.Columns[index].SortDirection = sortDirection;

            AttachSelection(displayedRoot);

            if (sortRequested)
                SetSortInfo($" · sortiert nach {ColumnName(sortColumn)} " +
                            $"({(sortDirection == ListSortDirection.Ascending ? "aufsteigend" : "absteigend")}, " +
                            $"{sw.Elapsed.TotalSeconds:N1} s)");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Anzeige aufbauen", ex);
            ReportError(ex.Message);
        }
        finally
        {
            if (version == _rebuildVersion)
                SetRebuilding(false);
            NotifyButtonStates();
        }
    }

    private void SetSortInfo(string value)
    {
        _sortInfo = value;
        OnPropertyChanged(nameof(StatusDisplay));
    }

    private void SetRebuilding(bool value)
    {
        _isRebuilding = value;
        OnPropertyChanged(nameof(IsBusy));
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
        if (index < 0 || _root is null)
            return false;

        // Richtung selbst bestimmen (die vom Grid vorgeschlagene passt nach dem Neuaufbau nicht
        // immer): erneuter Klick auf dieselbe Spalte dreht um; eine neue Spalte beginnt bei
        // Zahlen absteigend (größte zuerst), bei Name/Typ aufsteigend (A–Z).
        direction = index == _sortColumnIndex
            ? (_sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending)
            : (index >= 2 ? ListSortDirection.Descending : ListSortDirection.Ascending);

        _sortColumnIndex = index;
        _sortDirection = direction;
        column.SortDirection = direction; // Pfeil sofort zeigen
        _ = RebuildViewAsync(sortRequested: true);
        return true;
    }

    private static string ColumnName(int? index) => index switch
    {
        0 => "Name",
        1 => "Typ",
        2 => "Größe",
        3 => "Dateien",
        4 => "Ordner",
        5 => "Anteil",
        _ => "Standard"
    };

    /// <summary>Läuft im Hintergrund: Kopie für die Anzeige erstellen, filtern, sortieren.</summary>
    private static FolderNode? BuildDisplayTree(
        FolderNode root,
        string search,
        int depth,
        int? sortColumn,
        ListSortDirection sortDirection)
    {
        FolderNode? displayedRoot;
        if (search.Length == 0)
        {
            displayedRoot = root.CreateDepthLimitedCopy(depth);
        }
        else
        {
            displayedRoot = root.CreateFilteredCopy(search, depth);
            var rootMatches = root.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
            if (!rootMatches && displayedRoot.Children.Count == 0)
                return null;
        }

        if (sortColumn is { } column)
            TreeSorter.Sort(displayedRoot, column, sortDirection);

        return displayedRoot;
    }

    /// <summary>
    /// Merkt sich den im Baum ausgewählten Eintrag (als Pfad, weil die Anzeige mit Kopien der
    /// Knoten arbeitet) und stellt die Auswahl nach einem Neuaufbau des Baums wieder her.
    /// </summary>
    private void AttachSelection(FolderNode? displayedRoot)
    {
        var selection = Source.RowSelection;
        if (selection is null)
            return;

        if (displayedRoot is not null && _selectedPath is not null &&
            TryGetIndexPath(displayedRoot, _selectedPath, out var indexPath))
        {
            selection.SelectedIndex = indexPath;
        }
        else
        {
            _selectedPath = null;
        }

        selection.SelectionChanged += (_, _) =>
        {
            _selectedPath = selection.SelectedItem?.FullPath;
            NotifyExportScope();
        };
    }

    private static bool TryGetIndexPath(FolderNode displayedRoot, string path, out IndexPath indexPath)
    {
        var indices = new List<int> { 0 };
        var node = displayedRoot;

        while (!PathEquals(node.FullPath, path))
        {
            var next = -1;
            for (var index = 0; index < node.Children.Count; index++)
            {
                if (IsSameOrInside(path, node.Children[index].FullPath))
                {
                    next = index;
                    break;
                }
            }

            if (next < 0)
            {
                indexPath = default;
                return false;
            }

            indices.Add(next);
            node = node.Children[next];
        }

        indexPath = new IndexPath(indices.ToArray());
        return true;
    }

    /// <summary>Ordner, ab dem exportiert wird: Auswahl im Baum (Datei → ihr Ordner) oder der Scan.</summary>
    private FolderNode? GetExportNode()
    {
        var root = _root;
        if (root is null || _selectedPath is null)
            return root;

        var node = FindNode(root, _selectedPath);
        if (node is { IsFolder: false })
            node = FindNode(root, Path.GetDirectoryName(node.FullPath) ?? root.FullPath);

        return node ?? root;
    }

    private static FolderNode? FindNode(FolderNode root, string path)
    {
        var node = root;
        while (!PathEquals(node.FullPath, path))
        {
            FolderNode? next = null;
            foreach (var child in node.Children)
            {
                if (IsSameOrInside(path, child.FullPath))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
                return null;
            node = next;
        }

        return node;
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>true, wenn <paramref name="path"/> gleich <paramref name="candidate"/> ist oder darin liegt.</summary>
    private static bool IsSameOrInside(string path, string candidate)
    {
        if (PathEquals(path, candidate))
            return true;

        var prefix = Path.TrimEndingDirectorySeparator(candidate) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private void NotifyExportScope()
    {
        OnPropertyChanged(nameof(ExportScopeText));
        OnPropertyChanged(nameof(ExportScopePath));
    }

    private SortableTreeSource CreateSource(IEnumerable<FolderNode> roots)
    {
        var sizeOptions = CreateLongOptions(node => node.SizeBytes);
        var fileOptions = CreateLongOptions(node => node.FileCount);
        var folderOptions = CreateLongOptions(node => node.FolderCount);
        var percentOptions = CreateDoubleOptions(node => node.Percent);

        return new SortableTreeSource(roots, OnSortRequested)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<FolderNode>(
                    new TemplateColumn<FolderNode>(
                        "Name",
                        CreateNameCellTemplate(),
                        null,
                        new GridLength(1, GridUnitType.Star),
                        new TemplateColumnOptions<FolderNode>
                        {
                            IsTextSearchEnabled = true,
                            TextSearchValueSelector = node => node.Name,
                            CompareAscending = (left, right) => CompareNames(left, right),
                            CompareDescending = (left, right) => CompareNames(right, left)
                        }),
                    node => node.Children),
                new TextColumn<FolderNode, string>(
                    "Typ",
                    node => node.TypeText,
                    new GridLength(130),
                    new TextColumnOptions<FolderNode>
                    {
                        CompareAscending = (left, right) => string.Compare(left?.TypeText, right?.TypeText, StringComparison.CurrentCultureIgnoreCase),
                        CompareDescending = (left, right) => string.Compare(right?.TypeText, left?.TypeText, StringComparison.CurrentCultureIgnoreCase)
                    }),
                new TextColumn<FolderNode, string>(
                    "Größe",
                    node => node.SizeText,
                    new GridLength(145),
                    sizeOptions),
                new TextColumn<FolderNode, string>(
                    "Dateien",
                    node => node.FileCountText,
                    new GridLength(110),
                    fileOptions),
                new TextColumn<FolderNode, string>(
                    "Ordner",
                    node => node.FolderCountText,
                    new GridLength(100),
                    folderOptions),
                new TextColumn<FolderNode, string>(
                    "Anteil",
                    node => node.PercentText,
                    new GridLength(90),
                    percentOptions)
            }
        };
    }

    private static IDataTemplate CreateNameCellTemplate() =>
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
                    : Brush.Parse("#64748B")
            };

            var name = new TextBlock
            {
                Text = node.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
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
        OnPropertyChanged(nameof(CanExport));
        NotifyExportScope();
        OnPropertyChanged(nameof(CanExpandOneLevel));
        OnPropertyChanged(nameof(CanCollapseOneLevel));
        OnPropertyChanged(nameof(ExpansionDepthText));
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
