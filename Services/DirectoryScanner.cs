using TreeSizeViewNG.Models;

namespace TreeSizeViewNG.Services;

public sealed record ScanProgress(
    long Files,
    long Folders,
    long Bytes,
    long SkippedEntries,
    string CurrentPath);

public sealed class DirectoryScanner
{
    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public Task<FolderNode> ScanAsync(
        string rootPath,
        long minimumFileSize,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => ScanFolder(rootPath, Math.Max(0, minimumFileSize), progress, cancellationToken),
            cancellationToken);

    private static FolderNode ScanFolder(
        string path,
        long minimumFileSize,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var root = new FolderNode
        {
            Name = GetDisplayName(path),
            FullPath = path
        };

        var stack = new Stack<WorkItem>();
        stack.Push(new WorkItem(root, path, false));

        long totalFiles = 0;
        long totalFolders = 0;
        long totalBytes = 0;
        long skippedEntries = 0;
        var lastReport = Environment.TickCount64;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = stack.Pop();

            if (item.Finalize)
            {
                foreach (var child in item.Node.Children)
                {
                    item.Node.SizeBytes += child.SizeBytes;
                    item.Node.FileCount += child.FileCount;
                    item.Node.FolderCount += child.FolderCount + (child.IsFolder ? 1 : 0);
                }

                continue;
            }

            stack.Push(item with { Finalize = true });

            ScanFiles(
                item,
                minimumFileSize,
                cancellationToken,
                ref totalFiles,
                ref totalBytes,
                ref skippedEntries);

            var childNodes = ScanDirectories(
                item,
                cancellationToken,
                ref totalFolders,
                ref skippedEntries);

            for (var index = childNodes.Count - 1; index >= 0; index--)
            {
                var child = childNodes[index];
                stack.Push(new WorkItem(child, child.FullPath, false));
            }

            if (Environment.TickCount64 - lastReport >= 150)
            {
                progress?.Report(new ScanProgress(
                    totalFiles,
                    totalFolders,
                    totalBytes,
                    skippedEntries,
                    item.Path));
                lastReport = Environment.TickCount64;
            }
        }

        SetPercentages(root);
        SortChildrenBySize(root);

        progress?.Report(new ScanProgress(
            totalFiles,
            totalFolders,
            root.SizeBytes,
            skippedEntries,
            path));

        return root;
    }

    private static void ScanFiles(
        WorkItem item,
        long minimumFileSize,
        CancellationToken cancellationToken,
        ref long totalFiles,
        ref long totalBytes,
        ref long skippedEntries)
    {
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(item.Path, "*", ScanOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var length = new FileInfo(filePath).Length;
                    totalFiles++;

                    if (length >= minimumFileSize)
                    {
                        item.Node.Children.Add(new FolderNode
                        {
                            Name = Path.GetFileName(filePath),
                            FullPath = filePath,
                            IsFolder = false,
                            SizeBytes = length,
                            FileCount = 1
                        });
                        totalBytes += length;
                    }
                }
                catch (IOException)
                {
                    skippedEntries++;
                }
                catch (UnauthorizedAccessException)
                {
                    skippedEntries++;
                }
            }
        }
        catch (IOException)
        {
            skippedEntries++;
        }
        catch (UnauthorizedAccessException)
        {
            skippedEntries++;
        }
    }

    private static List<FolderNode> ScanDirectories(
        WorkItem item,
        CancellationToken cancellationToken,
        ref long totalFolders,
        ref long skippedEntries)
    {
        var children = new List<FolderNode>();

        try
        {
            foreach (var directoryPath in Directory.EnumerateDirectories(item.Path, "*", ScanOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Zusätzliche Prüfung für Plattformen/Dateisysteme, auf denen
                    // EnumerationOptions einen Link nicht sicher herausfiltert.
                    var attributes = File.GetAttributes(directoryPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    var child = new FolderNode
                    {
                        Name = GetDisplayName(directoryPath),
                        FullPath = directoryPath
                    };

                    item.Node.Children.Add(child);
                    children.Add(child);
                    totalFolders++;
                }
                catch (IOException)
                {
                    skippedEntries++;
                }
                catch (UnauthorizedAccessException)
                {
                    skippedEntries++;
                }
            }
        }
        catch (IOException)
        {
            skippedEntries++;
        }
        catch (UnauthorizedAccessException)
        {
            skippedEntries++;
        }

        return children;
    }

    private static void SetPercentages(FolderNode root)
    {
        var rootSize = Math.Max(1, root.SizeBytes);
        var stack = new Stack<FolderNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            node.Percent = node.SizeBytes * 100d / rootSize;

            foreach (var child in node.Children)
                stack.Push(child);
        }
    }

    private static void SortChildrenBySize(FolderNode root)
    {
        var stack = new Stack<FolderNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (node.Children.Count > 1)
            {
                var sorted = node.Children
                    .OrderByDescending(child => child.IsFolder)
                    .ThenByDescending(child => child.SizeBytes)
                    .ThenBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();

                node.Children.Clear();
                foreach (var child in sorted)
                    node.Children.Add(child);
            }

            foreach (var child in node.Children)
                stack.Push(child);
        }
    }

    private static string GetDisplayName(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : path;
    }

    private sealed record WorkItem(FolderNode Node, string Path, bool Finalize);
}
