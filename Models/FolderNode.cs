using System.Collections.ObjectModel;
using Material.Icons;
using TreeSizeViewNG.Localization;

namespace TreeSizeViewNG.Models;

public sealed class FolderNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; } = true;
    public long SizeBytes { get; set; }
    public long FileCount { get; set; }
    public long FolderCount { get; set; }
    public double Percent { get; set; }
    public ObservableCollection<FolderNode> Children { get; } = [];

    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string FileCountText => IsFolder ? FileCount.ToString("N0") : "1";
    public string FolderCountText => IsFolder ? FolderCount.ToString("N0") : string.Empty;
    public string PercentText => $"{Percent:N1} %";
    public MaterialIconKind IconKind => FileTypeIconResolver.GetIconKind(this);
    public string TypeText => IsFolder ? Strings.TypeFolder : FileTypeIconResolver.GetTypeText(FullPath);

    public FolderNode CreateFilteredCopy(string searchText, int depthLimit)
    {
        var trimmed = searchText?.Trim() ?? string.Empty;
        var comparison = StringComparison.OrdinalIgnoreCase;

        var copy = CloneWithoutChildren();
        if (depthLimit <= 0)
            return copy;

        foreach (var child in Children)
        {
            var childCopy = child.CreateFilteredCopy(trimmed, depthLimit - 1);
            var childMatches = child.Name.Contains(trimmed, comparison);
            if (childMatches || childCopy.Children.Count > 0)
                copy.Children.Add(childCopy);
        }

        return copy;
    }

    public FolderNode CreateDepthLimitedCopy(int depthLimit)
    {
        var copy = CloneWithoutChildren();
        if (depthLimit <= 0)
            return copy;

        foreach (var child in Children)
            copy.Children.Add(child.CreateDepthLimitedCopy(depthLimit - 1));

        return copy;
    }

    public int GetMaxDepth()
    {
        if (Children.Count == 0)
            return 0;

        var maxChildDepth = 0;
        foreach (var child in Children)
            maxChildDepth = Math.Max(maxChildDepth, child.GetMaxDepth());

        return maxChildDepth + 1;
    }

    private FolderNode CloneWithoutChildren() => new()
    {
        Name = Name,
        FullPath = FullPath,
        IsFolder = IsFolder,
        SizeBytes = SizeBytes,
        FileCount = FileCount,
        FolderCount = FolderCount,
        Percent = Percent
    };
}

public static class FileTypeIconResolver
{
    private static readonly Dictionary<string, string> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "FileDocumentOutline", [".log"] = "FileDocumentOutline", [".md"] = "LanguageMarkdownOutline",
        [".rtf"] = "FileDocumentOutline", [".csv"] = "FileDelimitedOutline",
        [".pdf"] = "FilePdfBox", [".doc"] = "MicrosoftWord", [".docx"] = "MicrosoftWord",
        [".xls"] = "MicrosoftExcel", [".xlsx"] = "MicrosoftExcel", [".ods"] = "Table",
        [".ppt"] = "MicrosoftPowerpoint", [".pptx"] = "MicrosoftPowerpoint",
        [".jpg"] = "FileImageOutline", [".jpeg"] = "FileImageOutline", [".png"] = "FileImageOutline",
        [".gif"] = "FileGifBox", [".bmp"] = "FileImageOutline", [".webp"] = "FileImageOutline",
        [".svg"] = "Svg", [".ico"] = "FileImageOutline", [".tif"] = "FileImageOutline", [".tiff"] = "FileImageOutline",
        [".mp3"] = "FileMusicOutline", [".wav"] = "FileMusicOutline", [".flac"] = "FileMusicOutline",
        [".ogg"] = "FileMusicOutline", [".m4a"] = "FileMusicOutline", [".aac"] = "FileMusicOutline",
        [".mp4"] = "FileVideoOutline", [".mkv"] = "FileVideoOutline", [".avi"] = "FileVideoOutline",
        [".mov"] = "FileVideoOutline", [".webm"] = "FileVideoOutline", [".wmv"] = "FileVideoOutline",
        [".zip"] = "FolderZipOutline", [".7z"] = "FolderZipOutline", [".rar"] = "FolderZipOutline",
        [".tar"] = "FolderZipOutline", [".gz"] = "FolderZipOutline", [".bz2"] = "FolderZipOutline", [".xz"] = "FolderZipOutline",
        [".cs"] = "LanguageCsharp", [".fs"] = "LanguageFsharp", [".vb"] = "LanguageVisualBasic",
        [".cpp"] = "LanguageCpp", [".c"] = "LanguageC", [".h"] = "LanguageC",
        [".java"] = "LanguageJava", [".kt"] = "LanguageKotlin", [".py"] = "LanguagePython",
        [".js"] = "LanguageJavascript", [".ts"] = "LanguageTypescript", [".html"] = "LanguageHtml5",
        [".css"] = "LanguageCss3", [".json"] = "CodeJson", [".xml"] = "Xml", [".yaml"] = "FileCodeOutline",
        [".yml"] = "FileCodeOutline", [".sh"] = "Console", [".bat"] = "Console", [".cmd"] = "Console",
        [".exe"] = "ApplicationOutline", [".msi"] = "PackageVariantClosed", [".appimage"] = "ApplicationOutline",
        [".dll"] = "FileCogOutline", [".so"] = "FileCogOutline", [".dylib"] = "FileCogOutline",
        [".iso"] = "Disc", [".img"] = "Disc", [".vhd"] = "Harddisk", [".vhdx"] = "Harddisk",
        [".db"] = "DatabaseOutline", [".sqlite"] = "DatabaseOutline", [".sql"] = "DatabaseOutline",
        [".ttf"] = "FormatFont", [".otf"] = "FormatFont", [".woff"] = "FormatFont", [".woff2"] = "FormatFont"
    };

    public static MaterialIconKind GetIconKind(FolderNode node)
    {
        var name = node.IsFolder
            ? "Folder"
            : Icons.GetValueOrDefault(Path.GetExtension(node.FullPath), "FileOutline");

        if (Enum.TryParse<MaterialIconKind>(name, true, out var icon))
            return icon;

        if (Enum.TryParse<MaterialIconKind>(node.IsFolder ? "FolderOutline" : "File", true, out icon))
            return icon;

        return default;
    }

    public static string GetTypeText(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
            return Strings.TypeFile;

        return Strings.FormatTypeFile(extension.TrimStart('.').ToUpperInvariant());
    }
}

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:N2} {Units[unit]}";
    }
}
