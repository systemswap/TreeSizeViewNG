using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
///
/// Die Linien sind anklickbar: Ein Klick wählt den Ordner, unter dessen Pfeil die Linie beginnt –
/// damit ist, wie bei einem Klick auf seine Zeile, die ganze Linie dieser Ebene hervorgehoben.
/// </summary>
public sealed class IndentGuides : Control
{
    public const double LevelWidth = 18;
    private const double LineOffset = 11; // Mitte des Aufklapp-Pfeils der jeweiligen Ebene

    private static readonly IPen Thin = new Pen(new SolidColorBrush(Color.FromArgb(110, 138, 147, 160)), 1);
    private static readonly IPen Active = new Pen(new SolidColorBrush(Color.FromRgb(91, 141, 239)), 2);
    private static Cursor? _handCursor;

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
    private readonly Action<FolderNode>? _lineClicked;

    /// <param name="node">Eintrag der Zeile; seine Tiefe bestimmt die Zahl der Linien.</param>
    /// <param name="lineClicked">Wird beim Klick auf eine Linie mit dem Ordner aufgerufen, zu dem sie gehört;
    /// <c>null</c>: Linien nicht anklickbar.</param>
    public IndentGuides(FolderNode node, Action<FolderNode>? lineClicked = null)
    {
        _node = node;
        _lineClicked = lineClicked;
        Width = node.Depth * LevelWidth;
        ClipToBounds = false;
        if (lineClicked is not null && node.Depth > 0)
            Cursor = _handCursor ??= new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// Ordner, zu dem die Linie nächst <paramref name="x"/> gehört: Die Linie der Ebene L beginnt unter dem
    /// Pfeil des Vorfahren mit Tiefe L. Jede Linie nimmt die ganze Breite ihrer Ebene an, damit man die
    /// 1–2 Pixel dünne Linie nicht genau treffen muss.
    /// </summary>
    /// <param name="node">Eintrag der Zeile, in der geklickt wurde.</param>
    /// <param name="x">Waagrechte Position in der Einrückung, ab ihrem linken Rand.</param>
    /// <returns>Der Vorfahr; <c>null</c> für Einträge ohne Einrückung (Stammordner).</returns>
    public static FolderNode? OwnerOfLineAt(FolderNode node, double x)
    {
        if (node.Depth == 0)
            return null;

        var level = Math.Clamp((int)Math.Round((x - LineOffset) / LevelWidth), 0, node.Depth - 1);
        var owner = node.Parent;
        while (owner is not null && owner.Depth > level)
            owner = owner.Parent;
        return owner;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_lineClicked is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (OwnerOfLineAt(_node, e.GetPosition(this).X) is not { } owner)
            return;

        // erledigt: die Zeile selbst soll nicht zusätzlich gewählt werden
        e.Handled = true;
        _lineClicked(owner);
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

        // Avalonia trifft beim Klicken nur Gezeichnetes: durchsichtige Fläche, damit die ganze Einrückung anklickbar ist
        if (_lineClicked is not null)
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

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
