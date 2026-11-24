#:package SkiaSharp@2.88.9
// Erzeugt das Programmsymbol (Stil wie PDF-Knead: abgerundete, einfarbige Fläche mit weißem Motiv):
//   Assets/Icon.png  (256 px, z. B. für Linux-Menüs und README)
//   Assets/app.ico   (Windows: 16, 24, 32, 48, 64, 128, 256 px als PNG-Einträge)
// Motiv: Ordner mit drei Größenbalken (groß → klein) = "Ordnergrößen, nach Größe sortiert".
//
// Aufruf aus dem Projektordner:  dotnet run --file tools/make-icon.cs
using SkiaSharp;

var assets = Path.Combine(Directory.GetCurrentDirectory(), "Assets");
Directory.CreateDirectory(assets);

int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var pngs = sizes.ToDictionary(s => s, Render);

File.WriteAllBytes(Path.Combine(assets, "Icon.png"), pngs[256]);
File.WriteAllBytes(Path.Combine(assets, "app.ico"), Ico(pngs));
Console.WriteLine($"Geschrieben: {Path.Combine(assets, "Icon.png")} und app.ico ({string.Join(", ", sizes)} px)");

static byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);
    c.Scale(size / 256f);

    using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

    // Hintergrund: abgerundetes Quadrat
    fill.Color = SKColor.Parse("#2E62D9");
    c.DrawRoundRect(new SKRect(0, 0, 256, 256), 56, 56, fill);

    // Ordner: Reiter und Körper
    fill.Color = SKColor.Parse("#DCE6FB");
    c.DrawRoundRect(new SKRect(48, 62, 128, 104), 12, 12, fill);
    fill.Color = SKColors.White;
    c.DrawRoundRect(new SKRect(48, 80, 208, 200), 16, 16, fill);

    // Größenbalken, nach Größe absteigend (bei kleinen Größen etwas dicker, damit sichtbar)
    var barHeight = size <= 24 ? 22f : 16f;
    (float Width, string Color)[] bars = [(112, "#2E62D9"), (80, "#6F93E6"), (48, "#A9C0F2")];
    var top = 106f;
    var step = size <= 24 ? 28f : 26f;
    foreach (var (width, color) in bars)
    {
        fill.Color = SKColor.Parse(color);
        c.DrawRoundRect(new SKRect(72, top, 72 + width, top + barHeight), barHeight / 2, barHeight / 2, fill);
        top += step;
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// ICO mit PNG-Einträgen (seit Windows Vista unterstützt): Kopf, Verzeichnis, Bilder.
static byte[] Ico(IReadOnlyDictionary<int, byte[]> pngs)
{
    using var stream = new MemoryStream();
    using var w = new BinaryWriter(stream);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)pngs.Count);
    var offset = 6 + 16 * pngs.Count;
    foreach (var (size, png) in pngs.OrderBy(p => p.Key))
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(png.Length);
        w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in pngs.OrderBy(p => p.Key))
        w.Write(png);
    return stream.ToArray();
}
