using System.IO.Enumeration;
using TreeSizeViewNG.Models;

namespace TreeSizeViewNG.Services;

public sealed record ScanProgress(
    long Files,
    long Folders,
    long Bytes,
    long SkippedEntries,
    string CurrentPath,
    int MaxDepth = 0);

/// <summary>
/// Scannt einen Ordner rekursiv (iterativ, ohne Rekursion im Aufrufstapel).
///
/// Schnell und sparsam: Jeder Ordner wird genau einmal aufgelistet; Name, Größe und Attribute kommen
/// direkt aus der Auflistung (kein Extra-Zugriff je Datei, keine FileInfo-Objekte). Knoten speichern
/// keinen eigenen Pfad, nur Name und Elternknoten.
///
/// Live-Summen: Größen und Anzahlen werden sofort die Elternkette hinauf addiert. Die Kinderliste eines
/// Ordners wird fertig aufgebaut und dann in einem Schritt veröffentlicht – die Oberfläche kann deshalb
/// schon während des Scans die obersten Ordner mit wachsenden Größen zeigen.
///
/// Reparse Points: Viele normale Dateien und Ordner sind Reparse Points, z. B. alles in OneDrive,
/// SharePoint oder anderen Sync-Ordnern (Cloud-Markierung). Sie werden mitgezählt. Übersprungen werden
/// nur echte Verknüpfungen (Junctions, symbolische Links), damit nichts doppelt gezählt wird und keine
/// Endlosschleifen entstehen.
///
/// Nur online: Platzhalter, deren Inhalt erst beim Öffnen geladen wird (Attribute Offline,
/// RecallOnOpen, RecallOnDataAccess), belegen keinen Platz auf der Platte. Sie werden angezeigt,
/// aber nicht zu Größe und Anteil der Ordner gezählt, sondern getrennt geführt.
///
/// Nicht sichtbar sind Einträge, die gar nicht im Dateisystem liegen, sondern nur von einer
/// Explorer-Erweiterung eingeblendet werden – etwa nicht zwischengespeicherte Dateien eines
/// SOLIDWORKS-PDM-Tresors.
/// </summary>
public sealed class DirectoryScanner
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes NotLocal = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        // nichts vorab ausfiltern (auch versteckte/System-Dateien und Reparse Points zählen)
        AttributesToSkip = 0
    };

    private readonly record struct Entry(string Name, long Length, FileAttributes Attributes, bool IsDirectory);

    /// <param name="rootCreated">Wird (im Scan-Thread) aufgerufen, sobald der Stammknoten existiert – für die Live-Anzeige.</param>
    public Task<FolderNode> ScanAsync(
        string rootPath,
        long minimumFileSize,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        Action<FolderNode>? rootCreated = null) =>
        Task.Run(
            () => ScanFolder(rootPath, Math.Max(0, minimumFileSize), progress, rootCreated, cancellationToken),
            cancellationToken);

    private static FolderNode ScanFolder(
        string path,
        long minimumFileSize,
        IProgress<ScanProgress>? progress,
        Action<FolderNode>? rootCreated,
        CancellationToken cancellationToken)
    {
        var root = new FolderNode
        {
            Name = GetDisplayName(path),
            FullPath = path
        };
        rootCreated?.Invoke(root);

        var stack = new Stack<(FolderNode Node, string Path)>();
        stack.Push((root, path));

        long totalFiles = 0, totalFolders = 0, skipped = 0;
        var maxDepth = 0;
        var lastReport = Environment.TickCount64;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (node, directory) = stack.Pop();

            var children = new List<FolderNode>();
            long localBytes = 0, localFiles = 0, onlineBytes = 0, onlineFiles = 0, folders = 0;
            var childDepth = node.Depth + 1;

            try
            {
                var entries = new FileSystemEnumerable<Entry>(
                    directory,
                    static (ref FileSystemEntry e) => new Entry(e.FileName.ToString(), e.Length, e.Attributes, e.IsDirectory),
                    ScanOptions);

                foreach (var entry in entries)
                {
                    if (entry.IsDirectory)
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 &&
                            IsLink(Path.Combine(directory, entry.Name)))
                            continue;

                        children.Add(new FolderNode { Name = entry.Name, Parent = node, Depth = childDepth });
                        folders++;
                        continue;
                    }

                    totalFiles++;
                    if (entry.Length < minimumFileSize)
                        continue;

                    var onlineOnly = (entry.Attributes & NotLocal) != 0;
                    children.Add(new FolderNode
                    {
                        Name = entry.Name,
                        Parent = node,
                        Depth = childDepth,
                        IsFolder = false,
                        IsOnlineOnly = onlineOnly,
                        SizeBytes = entry.Length,
                        FileCount = onlineOnly ? 0 : 1
                    });

                    if (onlineOnly)
                    {
                        onlineBytes += entry.Length;
                        onlineFiles++;
                    }
                    else
                    {
                        localBytes += entry.Length;
                        localFiles++;
                    }
                }
            }
            catch (IOException)
            {
                skipped++;
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
            }

            totalFolders += folders;
            if (children.Count > 0 && childDepth > maxDepth)
                maxDepth = childDepth;
            node.SetChildren(children);

            // Summen sofort die Elternkette hinauf (Live-Anzeige; kein zweiter Durchlauf am Ende nötig)
            for (var p = node; p is not null; p = p.Parent)
            {
                p.SizeBytes += localBytes;
                p.FileCount += localFiles;
                p.FolderCount += folders;
                p.OnlineOnlyBytes += onlineBytes;
                p.OnlineOnlyFileCount += onlineFiles;
            }

            foreach (var child in children)
            {
                if (child.IsFolder)
                    stack.Push((child, Path.Combine(directory, child.Name)));
            }

            if (Environment.TickCount64 - lastReport >= 150)
            {
                progress?.Report(new ScanProgress(totalFiles, totalFolders, root.SizeBytes, skipped, directory, maxDepth));
                lastReport = Environment.TickCount64;
            }
        }

        SetPercentages(root);
        // Ordner zuerst, dann lokale Dateien, dann Online-Dateien, je nach Größe (parallel, siehe TreeSorter)
        TreeSorter.SortDefault(root);

        progress?.Report(new ScanProgress(totalFiles, totalFolders, root.SizeBytes, skipped, path, maxDepth));
        return root;
    }

    /// <summary>
    /// Echte Verknüpfung (Junction, symbolischer Link)? Nur dann hat ein Reparse Point ein Linkziel;
    /// Cloud-Ordner (OneDrive usw.) sind zwar ebenfalls Reparse Points, aber keine Verknüpfungen.
    /// </summary>
    private static bool IsLink(string directoryPath)
    {
        try
        {
            return new DirectoryInfo(directoryPath).LinkTarget is not null;
        }
        catch (IOException)
        {
            return true; // unklarer Reparse Point: vorsichtshalber nicht betreten
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void SetPercentages(FolderNode root)
    {
        var rootSize = Math.Max(1, root.SizeBytes);
        var stack = new Stack<FolderNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            node.Percent = node.IsOnlineOnly ? 0 : node.SizeBytes * 100d / rootSize;

            foreach (var child in node.Children)
                stack.Push(child);
        }
    }

    private static string GetDisplayName(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : path;
    }
}
