using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace GenerateIcon;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string outputDir = args.Length > 0 ? args[0] : ".";
        string icoPath = Path.Combine(outputDir, "app.ico");

        Console.WriteLine("Generating multi-monitor brightness icon...");

        // Generate icon images at standard ICO sizes
        var sizes = new[] { 16, 24, 32, 48, 64, 256 };
        var images = new List<(Bitmap bmp, int size)>();

        foreach (int size in sizes)
        {
            images.Add((DrawMonitorIcon(size), size));
        }

        // Write ICO file with all sizes
        WriteIco(icoPath, images);

        foreach (var (bmp, _) in images)
        {
            bmp.Dispose();
        }

        Console.WriteLine($"Icon written to: {icoPath}");
    }

    static Bitmap DrawMonitorIcon(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        float s = size / 64f;

        // Background rounded rectangle (dark slate)
        using (var bgBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
        using (var path = RoundedRect(2 * s, 2 * s, 60 * s, 60 * s, 10 * s))
        {
            g.FillPath(bgBrush, path);
        }

        // Monitor 1 (back-left) - subtle dark monitor
        DrawMonitor(g, 10 * s, 15 * s, 24 * s, 18 * s,
            Color.FromArgb(51, 65, 85), Color.FromArgb(30, 58, 95), s);

        // Monitor 2 (front-right, overlapping) - vibrant blue
        DrawMonitor(g, 28 * s, 12 * s, 26 * s, 20 * s,
            Color.FromArgb(37, 99, 235), Color.FromArgb(59, 130, 246), s);

        // Brightness sun/rays symbol (bottom-right quadrant)
        DrawSunSymbol(g, 42 * s, 40 * s, 8 * s, Color.FromArgb(250, 204, 21));

        // Stand for monitor 1
        using (var standPen = new Pen(Color.FromArgb(100, 116, 139), Math.Max(1.5f * s, 1)))
        {
            g.DrawLine(standPen, 22 * s, 33 * s, 22 * s, 39 * s);
            g.DrawLine(standPen, 16 * s, 39 * s, 28 * s, 39 * s);
        }

        // Stand for monitor 2
        using (var standPen = new Pen(Color.FromArgb(100, 116, 139), Math.Max(1.5f * s, 1)))
        {
            g.DrawLine(standPen, 41 * s, 32 * s, 41 * s, 46 * s);
            g.DrawLine(standPen, 35 * s, 46 * s, 47 * s, 46 * s);
        }

        return bmp;
    }

    static void DrawMonitor(Graphics g, float x, float y, float w, float h,
        Color bezel, Color screen, float s)
    {
        float radius = 2 * s;
        using (var bezelBrush = new SolidBrush(bezel))
        using (var path = RoundedRect(x, y, w, h, radius))
        {
            g.FillPath(bezelBrush, path);
        }

        float inset = Math.Max(2 * s, 1.5f);
        using (var screenBrush = new SolidBrush(screen))
        {
            g.FillRectangle(screenBrush, x + inset, y + inset, w - 2 * inset, h - 2 * inset);
        }
    }

    static void DrawSunSymbol(Graphics g, float cx, float cy, float radius, Color color)
    {
        using var brush = new SolidBrush(color);
        float penWidth = Math.Max(radius * 0.18f, 1f);
        using var pen = new Pen(color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        // Center circle
        float cr = radius * 0.35f;
        g.FillEllipse(brush, cx - cr, cy - cr, cr * 2, cr * 2);

        // 8 rays
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            float x1 = cx + (float)(Math.Cos(angle) * radius * 0.55);
            float y1 = cy + (float)(Math.Sin(angle) * radius * 0.55);
            float x2 = cx + (float)(Math.Cos(angle) * radius * 0.92);
            float y2 = cy + (float)(Math.Sin(angle) * radius * 0.92);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Writes a multi-image ICO file. Each image is stored as a PNG-compressed entry
    /// (supported on Windows Vista+).
    /// </summary>
    static void WriteIco(string path, List<(Bitmap bmp, int size)> images)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        // ICO header
        bw.Write((ushort)0);                  // Reserved
        bw.Write((ushort)1);                  // Type: 1 = ICO
        bw.Write((ushort)images.Count);       // Number of images

        // Collect PNG data for each image
        var pngDataList = new List<byte[]>();
        foreach (var (bmp, _) in images)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngDataList.Add(ms.ToArray());
        }

        // Write directory entries
        int headerSize = 6;
        int directorySize = 16 * images.Count;
        int dataOffset = headerSize + directorySize;

        for (int i = 0; i < images.Count; i++)
        {
            int size = images[i].size;
            byte[] pngData = pngDataList[i];

            bw.Write((byte)(size >= 256 ? 0 : size)); // Width (0 = 256)
            bw.Write((byte)(size >= 256 ? 0 : size)); // Height (0 = 256)
            bw.Write((byte)0);                         // Color palette
            bw.Write((byte)0);                         // Reserved
            bw.Write((ushort)1);                       // Color planes
            bw.Write((ushort)32);                      // Bits per pixel
            bw.Write((uint)pngData.Length);            // Image data size
            bw.Write((uint)dataOffset);                // Offset to image data

            dataOffset += pngData.Length;
        }

        // Write image data
        foreach (byte[] pngData in pngDataList)
        {
            bw.Write(pngData);
        }
    }
}
