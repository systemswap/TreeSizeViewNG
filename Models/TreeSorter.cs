using System.ComponentModel;
using System.Globalization;

namespace TreeSizeViewNG.Models;

/// <summary>Wonach eine Spalte sortiert (unabhängig davon, an welcher Stelle sie steht).</summary>
public enum SortColumn
{
    Name,
    Type,
    Local,
    Online,
    Total,
    Files,
    Folders,
    Share
}

/// <summary>
/// Sortiert die Kinder aller Ordner eines Baums.
///
/// Für Millionen Einträge ausgelegt:
///  - Die Kinder jedes Ordners sind unabhängig voneinander → Ordner werden parallel auf allen
///    CPU-Kernen sortiert.
///  - Sortiert wird direkt in der Kinderliste (Introsort), ohne Kopien und ohne Hilfsobjekte –
///    bei Millionen Einträgen kostet Speicheranforderung mehr als das Vergleichen.
///  - Namen in natürlicher Reihenfolge der Kultur ("Datei2" vor "Datei10").
/// Bei Gleichstand entscheidet der Name (aufsteigend).
/// Darf nur auf Bäumen laufen, die gerade nicht angezeigt werden.
/// </summary>
public static class TreeSorter
{
    private const CompareOptions NameOptions = CompareOptions.IgnoreCase | CompareOptions.NumericOrdering;

    public static bool IsNumeric(SortColumn key) => key is not (SortColumn.Name or SortColumn.Type);

    /// <summary>Sortiert nach einer Spalte.</summary>
    public static void Sort(FolderNode root, SortColumn key, ListSortDirection direction, ProgressCounter? progress = null)
    {
        var sign = direction == ListSortDirection.Descending ? -1 : 1;
        SortAll(root, progress, (children, compare) => SortFolder(children, key, sign, compare));
    }

    /// <summary>Standardreihenfolge nach dem Scan: Ordner zuerst, dann lokale Dateien, dann Online-Dateien, je nach Größe absteigend.</summary>
    public static void SortDefault(FolderNode root, ProgressCounter? progress = null) =>
        SortAll(root, progress, SortFolderDefault);

    private static void SortAll(FolderNode root, ProgressCounter? progress, Action<List<FolderNode>, CompareInfo> sortFolder)
    {
        var folders = new List<FolderNode>();
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            folders.Add(node);
            foreach (var child in node.Children)
            {
                if (child.IsFolder)
                    stack.Push(child);
            }
        }

        // Die Kultur des Aufrufers verwenden (Parallel-Threads haben dieselbe, aber sicher ist sicher)
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        Parallel.ForEach(
            folders,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            folder =>
            {
                if (folder.Children.Count > 1)
                    sortFolder(folder.Children, compare);
                progress?.Step(1 + folder.Children.Count(c => !c.IsFolder));
            });
    }

    private static void SortFolder(List<FolderNode> children, SortColumn key, int sign, CompareInfo compare)
    {
        int ByName(FolderNode a, FolderNode b) => compare.Compare(a.Name, b.Name, NameOptions);

        switch (key)
        {
            case SortColumn.Name:
                children.Sort((a, b) => sign * ByName(a, b));
                break;

            case SortColumn.Type:
                // Typtext je Eintrag nur einmal bilden
                var entries = new (string Type, FolderNode Node)[children.Count];
                for (var i = 0; i < entries.Length; i++)
                    entries[i] = (children[i].TypeText, children[i]);
                Array.Sort(entries, (a, b) =>
                {
                    var c = sign * compare.Compare(a.Type, b.Type, CompareOptions.IgnoreCase);
                    return c != 0 ? c : ByName(a.Node, b.Node);
                });
                for (var i = 0; i < entries.Length; i++)
                    children[i] = entries[i].Node;
                break;

            default:
                Func<FolderNode, long> number = key switch
                {
                    SortColumn.Online => n => n.OnlineBytes,
                    SortColumn.Total => n => n.TotalBytes,
                    SortColumn.Files => n => n.FileCount,
                    SortColumn.Folders => n => n.FolderCount,
                    _ => n => n.LocalBytes // Auf der Platte und Anteil (proportional)
                };
                children.Sort((a, b) =>
                {
                    var c = sign * number(a).CompareTo(number(b));
                    return c != 0 ? c : ByName(a, b);
                });
                break;
        }
    }

    private static void SortFolderDefault(List<FolderNode> children, CompareInfo compare) =>
        children.Sort((a, b) =>
        {
            // Ordner zuerst, dann lokale Dateien, dann Online-Dateien; je größte zuerst
            var c = b.IsFolder.CompareTo(a.IsFolder);
            if (c != 0) return c;
            c = a.IsOnlineOnly.CompareTo(b.IsOnlineOnly);
            if (c != 0) return c;
            c = b.SizeBytes.CompareTo(a.SizeBytes);
            return c != 0 ? c : compare.Compare(a.Name, b.Name, NameOptions);
        });
}
