using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using TreeSizeViewNG.Models;

namespace TreeSizeViewNG.ViewModels;

/// <summary>
/// TreeDataGrid-Quelle, die das Sortieren an das ViewModel abgibt.
///
/// Die eingebaute Sortierung des TreeDataGrid läuft im UI-Thread und ist bei aufgeklapptem Baum
/// sehr langsam (gemessen: 200.000 Dateien ≈ 10–16 s, 2 Mio. Dateien mehrere Minuten – die
/// Oberfläche friert so lange ein). Deshalb wird ITreeDataGridSource.SortBy hier neu
/// implementiert: Ein Klick auf eine Spaltenüberschrift landet im ViewModel, das die Daten im
/// Hintergrund sortiert und den Baum danach fertig sortiert neu anzeigt.
/// </summary>
public sealed class SortableTreeSource : HierarchicalTreeDataGridSource<FolderNode>, ITreeDataGridSource
{
    private readonly Func<IColumn, ListSortDirection, bool> _sortRequested;

    public SortableTreeSource(IEnumerable<FolderNode> roots, Func<IColumn, ListSortDirection, bool> sortRequested)
        : base(roots)
    {
        _sortRequested = sortRequested;
    }

    bool ITreeDataGridSource.SortBy(IColumn? column, ListSortDirection direction) =>
        column is not null && _sortRequested(column, direction);
}
