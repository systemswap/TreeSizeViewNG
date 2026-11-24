using System.Globalization;
using System.Text;
using TreeSizeViewNG.Localization;
using TreeSizeViewNG.Models;

namespace TreeSizeViewNG.Services;

/// <summary>
/// Exportiert den gescannten Baum als flache CSV-Tabelle (eine Zeile pro Ordner bzw. Datei).
///
/// Aufbau:
///   Typ | Größe (Bytes) | Größe (MB) | Anteil (%) | Dateien | Unterordner | Tiefe | Erweiterung | Name
///   | Ebene 1 … Ebene N | Vollständiger Pfad
///
/// "Ebene 1 … N" enthalten die Ordnerkette:
///   - Standard: Ebene 1 = exportierter Ordner (der im Baum ausgewählte bzw. der gescannte),
///     Ebene 2 = dessen Unterordner usw.
///   - Mit includeParentLevels: Ebene 1 = Laufwerk (z. B. "C:" oder "\\server\freigabe"),
///     danach alle übergeordneten Ordner bis zum exportierten Ordner und weiter nach unten.
/// Eine Ordnerzeile steht dabei selbst in ihrer Ebene, eine Datei steht nur in "Name".
/// "Tiefe" ist die Ebene des Eintrags (Ordner = seine Ebene, Datei = Ordner + 1).
/// "Anteil (%)" bezieht sich auf den exportierten Ordner (= 100 %).
///
/// Achtung bei Summen: Ordnerzeilen enthalten bereits die Summe ihrer Inhalte. Für Summen
/// über die ganze Tabelle daher nach Typ = "Datei" filtern.
/// </summary>
public static class CsvExporter
{
    public sealed record ExportResult(long Rows, int LevelColumns);

    public static Task<ExportResult> ExportAsync(
        FolderNode root,
        Stream destination,
        bool includeParentLevels,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(root, destination, includeParentLevels, cancellationToken), cancellationToken);

    private static ExportResult Export(
        FolderNode root,
        Stream destination,
        bool includeParentLevels,
        CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentCulture;
        var separator = GetSeparator(culture);
        // Ordnerkette bis einschließlich Export-Ordner:
        // absolut z. B. ["C:", "Users", "Max"], sonst nur ["Max"].
        var absoluteChain = SplitPath(root.FullPath);
        var rootChain = includeParentLevels || absoluteChain.Count == 0
            ? absoluteChain
            : [absoluteChain[^1]];
        var rootSize = Math.Max(1d, root.SizeBytes);
        var levelColumns = rootChain.Count + GetMaxFolderDepth(root);

        // UTF-8 mit BOM, damit Excel Umlaute korrekt erkennt.
        using var writer = new StreamWriter(destination, new UTF8Encoding(true), 1 << 16);
        var row = new List<string>(10 + levelColumns);

        row.AddRange([Strings.CsvType, Strings.CsvSizeBytes, Strings.CsvSizeMb, Strings.CsvShare, Strings.CsvFiles,
                      Strings.CsvSubfolders, Strings.CsvDepth, Strings.CsvExtension, Strings.CsvName]);
        for (var level = 1; level <= levelColumns; level++)
            row.Add(Strings.FormatCsvLevel(level));
        row.Add(Strings.CsvFullPath);
        WriteRow(writer, row, separator);

        long rows = 0;
        var folderChain = new List<string>(rootChain);
        var stack = new Stack<(FolderNode Node, int Depth, bool Leave)>();
        stack.Push((root, 0, false));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (node, depth, leave) = stack.Pop();

            if (leave)
            {
                folderChain.RemoveAt(folderChain.Count - 1);
                continue;
            }

            if (node.IsFolder && depth > 0)
                folderChain.Add(node.Name);

            row.Clear();
            row.Add(node.IsFolder ? Strings.TypeFolder : Strings.TypeFile);
            row.Add(node.SizeBytes.ToString(CultureInfo.InvariantCulture));
            row.Add((node.SizeBytes / 1024d / 1024d).ToString("0.##", culture));
            row.Add((node.SizeBytes * 100d / rootSize).ToString("0.###", culture));
            row.Add(node.FileCount.ToString(CultureInfo.InvariantCulture));
            row.Add(node.IsFolder ? node.FolderCount.ToString(CultureInfo.InvariantCulture) : string.Empty);
            var absoluteDepth = node.IsFolder ? folderChain.Count : folderChain.Count + 1;
            row.Add(absoluteDepth.ToString(CultureInfo.InvariantCulture));
            row.Add(node.IsFolder ? string.Empty : Path.GetExtension(node.Name).TrimStart('.').ToLowerInvariant());
            row.Add(node.Name);
            for (var level = 0; level < levelColumns; level++)
                row.Add(level < folderChain.Count ? folderChain[level] : string.Empty);
            row.Add(node.FullPath);

            WriteRow(writer, row, separator);
            rows++;

            if (!node.IsFolder)
                continue;

            if (depth > 0)
                stack.Push((node, depth, true));

            // Rückwärts pushen, damit die Reihenfolge der Baumansicht erhalten bleibt.
            for (var index = node.Children.Count - 1; index >= 0; index--)
                stack.Push((node.Children[index], depth + 1, false));
        }

        writer.Flush();
        return new ExportResult(rows, levelColumns);
    }

    /// <summary>
    /// Zerlegt einen Pfad in Laufwerk/Freigabe und Ordnernamen:
    /// "C:\Users\Max" → ["C:", "Users", "Max"], "\\srv\daten\A" → ["\\srv\daten", "A"].
    /// </summary>
    private static List<string> SplitPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parts = new List<string>();

        var pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        var rootName = pathRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (rootName.Length == 0 && pathRoot.Length > 0)
            rootName = pathRoot; // z. B. "/" unter Linux
        if (rootName.Length > 0)
            parts.Add(rootName);

        var rest = fullPath[pathRoot.Length..];
        parts.AddRange(rest.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries));

        return parts;
    }

    private static int GetMaxFolderDepth(FolderNode root)
    {
        var max = 0;
        var stack = new Stack<(FolderNode Node, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (!node.IsFolder)
                continue;

            max = Math.Max(max, depth);
            foreach (var child in node.Children)
                stack.Push((child, depth + 1));
        }

        return max;
    }

    /// <summary>
    /// Deutsches Excel erwartet ";" als Trenner und "," als Dezimalzeichen.
    /// Das Listentrennzeichen der Systemkultur passt daher am besten zum lokalen Excel.
    /// </summary>
    private static string GetSeparator(CultureInfo culture)
    {
        var separator = culture.TextInfo.ListSeparator;
        if (string.IsNullOrEmpty(separator) || separator == culture.NumberFormat.NumberDecimalSeparator)
            separator = ";";
        return separator;
    }

    private static void WriteRow(TextWriter writer, List<string> values, string separator)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
                writer.Write(separator);
            writer.Write(Escape(values[index], separator));
        }

        writer.Write("\r\n");
    }

    private static string Escape(string value, string separator)
    {
        if (value.Length == 0)
            return value;

        var needsQuotes = value.Contains(separator, StringComparison.Ordinal) ||
                          value.Contains('"') || value.Contains('\n') || value.Contains('\r');

        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
