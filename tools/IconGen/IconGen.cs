#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property PublishAot=false
#:property PublishTrimmed=false
// Generates src/PassKeeper/Assets/PassKeeper.ico (multi-size) from vector drawing.
// Usage: dotnet run tools/IconGen/IconGen.cs -- <output.ico> [preview.png]
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var output = args.Length > 0 ? args[0] : "PassKeeper.ico";
var preview = args.Length > 1 ? args[1] : null;
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

var frames = new List<(int Size, byte[] Data, bool Png)>();
Exception? error = null;
var thread = new Thread(() =>
{
    try
    {
        foreach (var s in sizes)
        {
            var bmp = Render(s);
            if (s >= 128)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                enc.Save(ms);
                frames.Add((s, ms.ToArray(), true));
            }
            else frames.Add((s, ToDib(bmp, s), false));
        }
        if (preview != null)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Render(512)));
            using var fs = File.Create(preview);
            enc.Save(fs);
        }
    }
    catch (Exception ex) { error = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (error != null) throw error;

using (var fs = File.Create(output))
using (var w = new BinaryWriter(fs))
{
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)frames.Count);
    var offset = 6 + 16 * frames.Count;
    foreach (var f in frames)
    {
        w.Write((byte)(f.Size >= 256 ? 0 : f.Size));
        w.Write((byte)(f.Size >= 256 ? 0 : f.Size));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(f.Data.Length);
        w.Write(offset);
        offset += f.Data.Length;
    }
    foreach (var f in frames) w.Write(f.Data);
}
Console.WriteLine($"Wrote {output} ({frames.Count} sizes)");

static BitmapSource Render(int size)
{
    var s = (double)size;
    var dv = new DrawingVisual();
    using (var dc = dv.RenderOpen())
    {
        var bg = new LinearGradientBrush(Color.FromRgb(0x7B, 0x61, 0xFF), Color.FromRgb(0x3D, 0x8B, 0xFF), new Point(0, 0), new Point(1, 1));
        var inset = s <= 24 ? 0 : s * 0.04;
        var radius = s * 0.24;
        dc.DrawRoundedRectangle(bg, null, new Rect(inset, inset, s - 2 * inset, s - 2 * inset), radius, radius);

        // Subtle top highlight
        var hl = new LinearGradientBrush(Color.FromArgb(0x40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.5, 0), new Point(0.5, 0.6));
        dc.DrawRoundedRectangle(hl, null, new Rect(inset, inset, s - 2 * inset, s - 2 * inset), radius, radius);

        // Keyhole
        var white = Brushes.White;
        var cx = s * 0.5;
        var r = s * (s <= 24 ? 0.17 : 0.155);
        var cy = s * 0.41;
        var keyhole = new GeometryGroup { FillRule = FillRule.Nonzero };
        keyhole.Children.Add(new EllipseGeometry(new Point(cx, cy), r, r));
        var top = cy + r * 0.35;
        var bottom = s * 0.75;
        var topHalf = s * (s <= 24 ? 0.075 : 0.06);
        var bottomHalf = s * (s <= 24 ? 0.13 : 0.115);
        var fig = new PathFigure { StartPoint = new Point(cx - topHalf, top), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(new Point(cx + topHalf, top), true));
        fig.Segments.Add(new LineSegment(new Point(cx + bottomHalf, bottom - s * 0.03), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cx + bottomHalf, bottom), new Point(cx + bottomHalf - s * 0.03, bottom), true));
        fig.Segments.Add(new LineSegment(new Point(cx - bottomHalf + s * 0.03, bottom), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cx - bottomHalf, bottom), new Point(cx - bottomHalf, bottom - s * 0.03), true));
        keyhole.Children.Add(new PathGeometry([fig]));
        dc.DrawGeometry(white, null, keyhole);
    }
    var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    rtb.Render(dv);
    return rtb;
}

static byte[] ToDib(BitmapSource bmp, int size)
{
    var stride = size * 4;
    var pixels = new byte[stride * size];
    new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write(40);
    w.Write(size);
    w.Write(size * 2);
    w.Write((ushort)1);
    w.Write((ushort)32);
    w.Write(0);
    w.Write(stride * size);
    w.Write(0);
    w.Write(0);
    w.Write(0);
    w.Write(0);
    for (var y = size - 1; y >= 0; y--) w.Write(pixels, y * stride, stride);
    var maskStride = ((size + 31) / 32) * 4;
    w.Write(new byte[maskStride * size]);
    return ms.ToArray();
}
