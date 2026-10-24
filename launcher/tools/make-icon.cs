#:package SkiaSharp@3.119.4

// Draws the launcher's icon, the Evergrove leaf on a dark green tile, into launcher/assets/icon.png (Linux and macOS
// packages) and launcher/src/Launcher/App.ico (Windows: the exe, the window and the installer).
//
//   dotnet run make-icon.cs
//
// The leaf is the same drawing as images/banner.svg and the launcher's logo (Resources/Icons.axaml, Icons.Leaf),
// original artwork for Evergrove.

using SkiaSharp;

var here = Path.GetDirectoryName(Path.GetFullPath("make-icon.cs"))!;
var launcher = Path.GetFullPath(Path.Combine(here, ".."));

byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    // The tile: rounded, dark forest green, with a thin lighter edge.
    var inset = size * 0.04f;
    var tile = new SKRect(inset, inset, size - inset, size - inset);
    var radius = size * 0.22f;
    using (var fill = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#12241a") })
        canvas.DrawRoundRect(tile, radius, radius, fill);
    using (var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, size * 0.02f), Color = SKColor.Parse("#355a42") })
        canvas.DrawRoundRect(tile, radius, radius, edge);

    // The leaf, drawn on a 32-unit grid and centred on the tile.
    var scale = size * 0.72f / 32f;
    canvas.Translate(size / 2f - 16 * scale, size / 2f - 16 * scale);
    canvas.Scale(scale);

    void Fill(string data, string color)
    {
        using var path = SKPath.ParseSvgPathData(data);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(color) };
        canvas.DrawPath(path, paint);
    }

    // The stem first, so it grows out from under the leaves.
    using (var stem = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, StrokeCap = SKStrokeCap.Round, Color = SKColor.Parse("#d9c38a") })
        canvas.DrawLine(16, 14, 16, 30, stem);
    Fill("M16 2 C24 8 26 17 16 24 C6 17 8 8 16 2 Z", "#8cc56a");
    Fill("M16 13 C21 10 26 11 29 14 C25 19 19 18 16 15 Z", "#5f9a46");
    Fill("M16 13 C11 10 6 11 3 14 C7 19 13 18 16 15 Z", "#5f9a46");

    using var image = surface.Snapshot();
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    return png.ToArray();
}

// Linux and macOS packages take one large PNG.
File.WriteAllBytes(Path.Combine(launcher, "assets", "icon.png"), Render(1024));

// Windows: an .ico holding PNG images at the usual sizes (Vista and later read PNG entries).
int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var images = sizes.Select(Render).ToArray();
using (var ico = new BinaryWriter(File.Create(Path.Combine(launcher, "src", "Launcher", "App.ico"))))
{
    ico.Write((ushort)0);            // reserved
    ico.Write((ushort)1);            // type: icon
    ico.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));   // width (0 means 256)
        ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));   // height
        ico.Write((byte)0);          // palette size
        ico.Write((byte)0);          // reserved
        ico.Write((ushort)1);        // colour planes
        ico.Write((ushort)32);       // bits per pixel
        ico.Write(images[i].Length);
        ico.Write(offset);
        offset += images[i].Length;
    }
    foreach (var image in images)
        ico.Write(image);
}

Console.WriteLine($"Wrote assets/icon.png and src/Launcher/App.ico ({string.Join(", ", sizes)} px).");
