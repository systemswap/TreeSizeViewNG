using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TreeSizeViewNG.Models;

namespace TreeSizeViewNG.ViewModels;

/// <summary>
/// Einrückung einer Baumzeile mit dünnen senkrechten Führungslinien – eine je übergeordneter Ebene,
/// wie die Einrückungslinien in einem Code-Editor. Über die Zeilen hinweg ergeben sie durchgehende
/// Linien, an denen man beim Scrollen eine Ebene verfolgen kann.
///
/// Hervorgehoben wird die Linie der Ebene des gewählten Ordners (sie beginnt unter seinem Pfeil):
/// bei allen Ordnern dieser Ebene dicker und farbig.
/// </summary>
public sealed class IndentGuides : Control
{
    public const double LevelWidth = 18;
    private const double LineOffset = 11; // Mitte des Aufklapp-Pfeils der jeweiligen Ebene

    private static readonly IPen Thin = new Pen(new SolidColorBrush(Color.FromArgb(110, 138, 147, 160)), 1);
    private static readonly IPen Active = new Pen(new SolidColorBrush(Color.FromRgb(91, 141, 239)), 2);

    private static FolderNode? _selected;
    private static event Action? SelectedChanged;

    /// <summary>Der im Baum gewählte Eintrag; alle sichtbaren Linien zeichnen sich bei Änderung neu.</summary>
    public static FolderNode? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
                return;
            _selected = value;
            SelectedChanged?.Invoke();
        }
    }

    private readonly FolderNode _node;

    public IndentGuides(FolderNode node)
    {
        _node = node;
        Width = node.Depth * LevelWidth;
        ClipToBounds = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SelectedChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SelectedChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var depth = _node.Depth;
        if (depth == 0)
            return;

        // etwas über die Zelle hinaus zeichnen, damit die Linien zwischen den Zeilen nicht unterbrochen sind
        const double overdraw = 6;
        var top = -overdraw;
        var bottom = Bounds.Height + overdraw;

        // Hervorgehoben wird die Linie der Ebene des gewählten Ordners (sie beginnt unter seinem Pfeil) –
        // durchgehend bei allen Ordnern dieser Ebene. Bei einer gewählten Datei ist es die Ebene ihres Ordners.
        var owner = _selected is { IsFolder: false } file ? file.Parent : _selected;
        var activeLevel = owner is null ? -1 : owner.Depth;

        for (var level = 0; level < depth; level++)
        {
            var pen = level == activeLevel ? Active : Thin;
            var x = Math.Floor(level * LevelWidth + LineOffset) + (pen.Thickness % 2 == 1 ? 0.5 : 0);
            context.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }
    }
}
