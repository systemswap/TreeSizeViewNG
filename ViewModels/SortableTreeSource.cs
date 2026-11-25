using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using TreeSizeViewNG.Models;

namespace TreeSizeViewNG.ViewModels;

/// <summary>
/// Quelle für das TreeDataGrid: eine FLACHE Liste der gerade sichtbaren Zeilen.
///
/// Das hierarchische Modell des TreeDataGrid legt für jeden aufgeklappten Ordner eigene Zeilenobjekte
/// an und verschiebt bei jedem Aufklappen die Gesamtliste; bei großen, tiefen Bäumen dauerte ein
/// Ebenenwechsel dadurch Minuten. Hier berechnet das ViewModel die sichtbaren Zeilen selbst (ein
/// Durchlauf über den Baum, Millisekunden auch bei Millionen Einträgen); Einrückung und Aufklapp-Pfeil
/// zeichnet die Namensspalte.
///
/// Auch das Sortieren geht ans ViewModel (ITreeDataGridSource.SortBy neu implementiert): Es sortiert
/// die Daten im Hintergrund, statt das Grid im UI-Thread sortieren zu lassen.
/// </summary>
public sealed class SortableTreeSource : FlatTreeDataGridSource<FolderNode>, ITreeDataGridSource
{
    private readonly Func<IColumn, ListSortDirection, bool> _sortRequested;

    public SortableTreeSource(Func<IColumn, ListSortDirection, bool> sortRequested)
        : base(new RowList([]))
    {
        _sortRequested = sortRequested;
    }

    /// <summary>Wonach jede Spalte sortiert (gleiche Reihenfolge wie die Spalten).</summary>
    public List<SortColumn> ColumnKeys { get; set; } = [];

    bool ITreeDataGridSource.SortBy(IColumn? column, ListSortDirection direction) =>
        column is not null && _sortRequested(column, direction);
}

/// <summary>Zeilenliste mit Bereichs-Einfügen und -Entfernen (ein Ereignis je Bereich statt je Zeile).</summary>
public sealed class RowList : ObservableCollection<FolderNode>
{
    public RowList(List<FolderNode> rows) : base(rows)
    {
    }

    public int IndexOf(FolderNode node, int start, int count) => ((List<FolderNode>)Items).IndexOf(node, start, count);

    public void InsertRange(int index, List<FolderNode> rows)
    {
        if (rows.Count == 0)
            return;

        ((List<FolderNode>)Items).InsertRange(index, rows);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, rows, index));
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0)
            return;

        var items = (List<FolderNode>)Items;
        var removed = items.GetRange(index, count);
        items.RemoveRange(index, count);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }
}
