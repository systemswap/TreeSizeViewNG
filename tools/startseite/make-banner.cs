#:package SkiaSharp@2.88.9
// Zeichnet das Banner der Startseite (README) im Stil von PDF-Knead: docs/bilder/banner.png (1280 × 440).
// Links Symbol, Name, Leitsatz und Merkmale, rechts angeschnitten der Screenshot
// docs/bilder/hauptfenster-dunkel.png (aufgenommen mit künstlichen Beispieldaten).
//
// Aufruf aus dem Projektordner:  dotnet run --file tools/startseite/make-banner.cs
using SkiaSharp;

const int W = 1280, H = 440;
var root = Directory.GetCurrentDirectory();
var icon = SKBitmap.Decode(Path.Combine(root, "Assets", "Icon.png"));
var shot = SKBitmap.Decode(Path.Combine(root, "docs", "bilder", "hauptfenster-dunkel.png"));

using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
var c = surface.Canvas;
c.Clear(SKColors.Transparent);

// Abgerundete Fläche als Rahmen für alles Weitere
var card = new SKRoundRect(new SKRect(0, 0, W, H), 22);
c.ClipRoundRect(card, antialias: true);

// Hintergrund: dunkler Verlauf, blauer Schein oben rechts und unten Mitte
using (var p = new SKPaint { IsAntialias = true })
{
    p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(W, H * 0.4f),
        [SKColor.Parse("#0f1115"), SKColor.Parse("#121520"), SKColor.Parse("#161b2c")], [0f, .45f, 1f], SKShaderTileMode.Clamp);
    c.DrawRect(0, 0, W, H, p);
    p.Shader = SKShader.CreateRadialGradient(new SKPoint(W * .98f, 0), 620,
        [SKColor.Parse("#2E62D9").WithAlpha(105), SKColors.Transparent], SKShaderTileMode.Clamp);
    c.DrawRect(0, 0, W, H, p);
    p.Shader = SKShader.CreateRadialGradient(new SKPoint(W * .72f, H * 1.1f), 420,
        [SKColor.Parse("#2E62D9").WithAlpha(45), SKColors.Transparent], SKShaderTileMode.Clamp);
    c.DrawRect(0, 0, W, H, p);
}

// Screenshot rechts: verkleinert, angeschnitten, mit Schatten und feinem Rahmen
const float scale = 0.74f;
var shotRect = SKRect.Create(668, 58, shot.Width * scale, shot.Height * scale);
using (var shadow = new SKPaint { IsAntialias = true, Color = SKColors.Black.WithAlpha(150), ImageFilter = SKImageFilter.CreateBlur(24, 24) })
    c.DrawRoundRect(new SKRoundRect(SKRect.Create(shotRect.Left, shotRect.Top + 14, shotRect.Width, shotRect.Height), 12), shadow);
c.Save();
c.ClipRoundRect(new SKRoundRect(shotRect, 12), antialias: true);
using (var img = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
    c.DrawBitmap(shot, shotRect, img);
c.Restore();
using (var frame = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = SKColor.Parse("#3a3f4b") })
    c.DrawRoundRect(new SKRoundRect(shotRect, 12), frame);

// Text links
SKTypeface Face(SKFontStyleWeight weight) =>
    SKTypeface.FromFamilyName("Segoe UI", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
    ?? SKTypeface.FromFamilyName("Inter", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
    ?? SKTypeface.Default;

using var text = new SKPaint { IsAntialias = true, Color = SKColors.White, SubpixelText = true };
const float left = 66;

// Symbol und Name
using (var img = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
    c.DrawBitmap(icon, SKRect.Create(left, 70, 64, 64), img);
text.Typeface = Face(SKFontStyleWeight.Bold);
text.TextSize = 50;
c.DrawText("TreeSizeViewNG", left + 82, 120, text);

// Leitsatz
text.TextSize = 30;
c.DrawText("Scannen. Sortieren. Exportieren.", left, 196, text);
c.DrawText("Wissen, wo der Speicher bleibt.", left, 236, text);

// Unterzeile
text.Typeface = Face(SKFontStyleWeight.Normal);
text.TextSize = 18;
text.Color = SKColor.Parse("#c6cbd5");
c.DrawText("Ordnergrößen auf einen Blick – für Windows und Linux.", left, 278, text);

// Merkmale als Chips mit farbigem Punkt
(string Label, string Dot)[] chips =
[
    ("Millionen Dateien", "#3fb97a"),
    ("CSV für Excel", "#38bdf8"),
    ("Hell & Dunkel", "#7fa3d1"),
    ("Avalonia · .NET 10", "#8b7cf6")
];
text.TextSize = 14;
text.Color = SKColor.Parse("#dde1e8");
float x = left, y = 322, h = 32;
using var chipFill = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#1d2028") };
using var chipLine = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = SKColor.Parse("#343843") };
using var dot = new SKPaint { IsAntialias = true };
foreach (var (label, color) in chips)
{
    var w = 12 + 8 + 8 + text.MeasureText(label) + 13;
    var r = new SKRoundRect(SKRect.Create(x, y, w, h), h / 2);
    c.DrawRoundRect(r, chipFill);
    c.DrawRoundRect(r, chipLine);
    dot.Color = SKColor.Parse(color);
    c.DrawCircle(x + 16, y + h / 2, 4, dot);
    c.DrawText(label, x + 28, y + h / 2 + 5, text);
    x += w + 9;
}

using var image = surface.Snapshot();
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
var outPath = Path.Combine(root, "docs", "bilder", "banner.png");
File.WriteAllBytes(outPath, data.ToArray());
Console.WriteLine($"Geschrieben: {outPath}");
