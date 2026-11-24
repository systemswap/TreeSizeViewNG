using System.ComponentModel;
using System.Globalization;

namespace TreeSizeViewNG.Models;

/// <summary>
/// Sortiert die Kinder aller Ordner eines (Anzeige-)Baums nach einer Spalte.
/// Läuft im Hintergrund-Thread auf einer Kopie, die noch nicht angezeigt wird.
/// Jeder Sortierschlüssel wird pro Eintrag nur einmal berechnet (LINQ OrderBy), die
/// Sortierung ist stabil und bei Gleichstand nach Name geordnet.
/// </summary>
public static class TreeSorter
{
    // Natürliche Reihenfolge: "Datei2" vor "Datei10".
    private static readonly StringComparer NameComparer = StringComparer.Create(
        CultureInfo.CurrentCulture,
        CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    /// <param name="column">0 Name, 1 Typ, 2 Größe, 3 Dateien, 4 Ordner, 5 Anteil</param>
    public static void Sort(FolderNode root, int column, ListSortDirection direction)
    {
        var descending = direction == ListSortDirection.Descending;
        var stack = new Stack<FolderNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Children.Count == 0)
                continue;

            if (node.Children.Count > 1)
            {
                var sorted = SortChildren(node.Children, column, descending);
                node.Children.Clear();
                foreach (var child in sorted)
                    node.Children.Add(child);
            }

            foreach (var child in node.Children)
            {
                if (child.IsFolder)
                    stack.Push(child);
            }
        }
    }

    private static FolderNode[] SortChildren(IEnumerable<FolderNode> children, int column, bool descending)
    {
        IOrderedEnumerable<FolderNode> ordered = column switch
        {
            0 => By(children, n => n.Name, NameComparer, descending),
            1 => By(children, n => n.TypeText, StringComparer.CurrentCultureIgnoreCase, descending),
            3 => By(children, n => n.FileCount, Comparer<long>.Default, descending),
            4 => By(children, n => n.FolderCount, Comparer<long>.Default, descending),
            // 2 Größe und 5 Anteil: beide nach Bytes (Anteil ist proportional zur Größe)
            _ => By(children, n => n.SizeBytes, Comparer<long>.Default, descending)
        };

        return column == 0
            ? ordered.ToArray()
            : ordered.ThenBy(n => n.Name, NameComparer).ToArray();
    }

    private static IOrderedEnumerable<FolderNode> By<TKey>(
        IEnumerable<FolderNode> items,
        Func<FolderNode, TKey> key,
        IComparer<TKey> comparer,
        bool descending) =>
        descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);
}
