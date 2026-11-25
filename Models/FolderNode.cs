using System.ComponentModel;
using Material.Icons;
using TreeSizeViewNG.Localization;

namespace TreeSizeViewNG.Models;

public sealed class FolderNode : INotifyPropertyChanged
{
    private string? _path;
    private bool _isExpanded;

    public required string Name { get; init; }

    /// <summary>
    /// Voller Pfad. Wird nur beim Stammordner gespeichert und sonst aus der Elternkette gebildet –
    /// das spart bei Millionen Dateien viel Speicher und Zeit beim Scan.
    /// </summary>
    public string FullPath
    {
        get => _path ?? (Parent is null ? Name : Path.Combine(Parent.FullPath, Name));
        init => _path = value;
    }

    public FolderNode? Parent { get; set; }

    /// <summary>Tiefe im Baum (Stammordner = 0).</summary>
    public int Depth { get; set; }

    /// <summary>Für die Anzeige: Ordner ist aufgeklappt (die sichtbare Zeilenliste baut das ViewModel).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, IsExpandedChanged);
        }
    }

    /// <summary>Für die Suche: Eintrag passt selbst oder enthält passende Einträge.</summary>
    public bool MatchesFilter { get; set; } = true;

    private static readonly PropertyChangedEventArgs IsExpandedChanged = new(nameof(IsExpanded));
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasChildren => IsFolder && (_children is { Count: > 0 } || FileCount + FolderCount + OnlineOnlyFileCount > 0);

    /// <summary>Setzt die fertige Kinderliste in einem Schritt (der Scanner veröffentlicht sie so für die Live-Anzeige).</summary>
    public void SetChildren(List<FolderNode> children) => _children = children;

    /// <summary>Setzt Parent und Depth im ganzen Teilbaum (für von Hand gebaute Bäume, z. B. in Tests).</summary>
    public void FixupTree()
    {
        var stack = new Stack<FolderNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.Children)
            {
                child.Parent = node;
                child.Depth = node.Depth + 1;
                stack.Push(child);
            }
        }
    }

    public bool IsFolder { get; init; } = true;

    /// <summary>
    /// Datei liegt nur online (Cloud-Platzhalter, z. B. OneDrive „Dateien bei Bedarf“): sie wird
    /// angezeigt, belegt aber keinen Platz auf der Platte und zählt deshalb nicht zu Größe/Anteil
    /// der Ordner. <see cref="SizeBytes"/> ist bei ihr die Dateigröße online.
    /// </summary>
    public bool IsOnlineOnly { get; init; }

    /// <summary>
    /// Eintrag stammt aus einem SOLIDWORKS-PDM-Tresor und liegt nicht auf der Platte (bei Dateien
    /// zusätzlich <see cref="IsOnlineOnly"/>; bei Ordnern: den Ordner gibt es nur im Tresor).
    /// </summary>
    public bool IsVaultOnly { get; init; }

    /// <summary>
    /// PDM: Tresor-Größe der Dateien, die AUCH lokal vorliegen (bei einer Datei ihre eigene Tresor-Größe,
    /// bei Ordnern die Summe). Sie erscheint in "In Cloud", zählt aber nicht zu "Gesamt" – die Datei
    /// belegt nur einmal Platz.
    /// </summary>
    public long VaultLocalBytes { get; set; }

    /// <summary>PDM: Anzahl der Dateien, deren lokale Größe von der Tresor-Größe abweicht (Datei: 0 oder 1).</summary>
    public int MismatchCount { get; set; }

    public bool HasSizeMismatch => MismatchCount > 0;

    /// <summary>PDM: Ordner enthält Dateien, die nur im Tresor liegen.</summary>
    public bool HasVaultOnlyContent { get; set; }

    public long SizeBytes { get; set; }
    public long FileCount { get; set; }
    public long FolderCount { get; set; }

    /// <summary>Bei Ordnern: Anzahl und Größe der enthaltenen Dateien, die nur online liegen.</summary>
    public long OnlineOnlyFileCount { get; set; }
    public long OnlineOnlyBytes { get; set; }

    public double Percent { get; set; }
    // Kinder als schlichte Liste: nach dem Scan ändert sich nur noch die Reihenfolge (beim Sortieren,
    // während die Liste nicht angezeigt wird), eine ObservableCollection je Knoten wäre bei Millionen
    // Dateien nur Ballast. Dateien teilen sich eine leere Liste (sie bekommen nie Kinder).
    private static readonly List<FolderNode> NoChildren = new(0);
    private List<FolderNode>? _children;

    public List<FolderNode> Children => _children ??= IsFolder ? [] : NoChildren;

    /// <summary>Ordner, der nur Online-Dateien enthält (nichts davon liegt auf der Platte).</summary>
    public bool IsOnlineOnlyFolder => IsFolder && FileCount == 0 && OnlineOnlyFileCount > 0;

    // Drei Größen: auf der Platte, nur online (Cloud-Platzhalter), gesamt
    public long LocalBytes => IsOnlineOnly ? 0 : SizeBytes;
    // "In Cloud": was nur online/im Tresor liegt, plus die Tresor-Größe lokal vorhandener PDM-Dateien.
    // "Gesamt": jede Datei einmal (lokale Größe, sonst die Größe online/im Tresor).
    private long CloudOnlyBytes => IsOnlineOnly ? SizeBytes : OnlineOnlyBytes;
    public long OnlineBytes => CloudOnlyBytes + VaultLocalBytes;
    public long TotalBytes => LocalBytes + CloudOnlyBytes;

    public string SizeText => SizeFormatter.Format(LocalBytes);
    public string LocalText => IsOnlineOnly ? "–" : SizeFormatter.Format(LocalBytes);
    public string OnlineText => OnlineBytes > 0 ? SizeFormatter.Format(OnlineBytes) : "–";
    public string TotalText => SizeFormatter.Format(TotalBytes);
    public string FileCountText => IsFolder ? FileCount.ToString("N0") : IsOnlineOnly ? "–" : "1";
    public string FolderCountText => IsFolder ? FolderCount.ToString("N0") : string.Empty;
    public string PercentText => IsOnlineOnly ? "–" : $"{Percent:N1} %";
    public MaterialIconKind IconKind => FileTypeIconResolver.GetIconKind(this);
    public string TypeText => IsFolder
        ? (IsVaultOnly || (IsOnlineOnlyFolder && HasVaultOnlyContent) ? Strings.TypeFolderInVault : IsOnlineOnlyFolder ? Strings.TypeFolderOnlineOnly : Strings.TypeFolder)
        : IsVaultOnly
            ? Strings.FormatInVault(FileTypeIconResolver.GetTypeText(Name))
            : IsOnlineOnly
                ? Strings.FormatOnlineOnly(FileTypeIconResolver.GetTypeText(Name))
                : FileTypeIconResolver.GetTypeText(Name);

    /// <summary>Gefilterte Kopie des Teilbaums (für den Export bei aktiver Suche).</summary>
    public FolderNode CreateFilteredCopy(string searchText) =>
        CreateFilteredCopy(searchText?.Trim() ?? string.Empty, FullPath);

    private FolderNode CreateFilteredCopy(string search, string? explicitPath)
    {
        var copy = new FolderNode
        {
            Name = Name,
            FullPath = explicitPath!, // null: Pfad ergibt sich aus der Elternkette der Kopie
            IsFolder = IsFolder,
            IsOnlineOnly = IsOnlineOnly,
            IsVaultOnly = IsVaultOnly,
            VaultLocalBytes = VaultLocalBytes,
            MismatchCount = MismatchCount,
            HasVaultOnlyContent = HasVaultOnlyContent,
            Depth = Depth,
            SizeBytes = SizeBytes,
            FileCount = FileCount,
            FolderCount = FolderCount,
            OnlineOnlyFileCount = OnlineOnlyFileCount,
            OnlineOnlyBytes = OnlineOnlyBytes,
            Percent = Percent
        };

        foreach (var child in Children)
        {
            var childCopy = child.CreateFilteredCopy(search, null);
            if (child.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || childCopy.Children.Count > 0)
            {
                childCopy.Parent = copy;
                copy.Children.Add(childCopy);
            }
        }

        return copy;
    }

    /// <summary>Anzahl aller Knoten (dieser plus alle darunter) – für Fortschrittsanzeigen.</summary>
    public long NodeCount => 1 + FolderCount + FileCount + OnlineOnlyFileCount;

    public int GetMaxDepth()
    {
        if (Children.Count == 0)
            return 0;

        var maxChildDepth = 0;
        foreach (var child in Children)
            maxChildDepth = Math.Max(maxChildDepth, child.GetMaxDepth());

        return maxChildDepth + 1;
    }
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
        // Nur online: Wolke statt Dateityp; Ordner ohne lokale Dateien: hohler Ordner.
        var name = node.IsFolder
            ? (node.IsOnlineOnlyFolder || node.IsVaultOnly ? "FolderOutline" : "Folder")
            : node.IsVaultOnly
                ? "Safe" // nur im PDM-Tresor
                : node.IsOnlineOnly
                    ? "CloudOutline"
                : Icons.GetValueOrDefault(Path.GetExtension(node.Name), "FileOutline");

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
