// This C# script generates the app icon. Run once with: dotnet-script GenerateIcon.csx
// Or just use the pre-built app.ico already in this folder.
// The icon depicts two overlapping monitor screens with a sun/brightness symbol.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

void DrawMonitorIcon(int size, string outputPath)
{
    using var bmp = new Bitmap(size, size);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    float s = size / 64f; // Scale factor from 64px reference

    // Background circle (dark blue-gray)
    using (var bgBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
    {
        g.FillEllipse(bgBrush, 2*s, 2*s, 60*s, 60*s);
    }

    // Monitor 1 (back, slightly left) - darker
    DrawMonitor(g, 10*s, 16*s, 26*s, 20*s, Color.FromArgb(51, 65, 85), Color.FromArgb(71, 85, 105), s);

    // Monitor 2 (front, slightly right) - lighter/primary
    DrawMonitor(g, 28*s, 14*s, 26*s, 20*s, Color.FromArgb(59, 130, 246), Color.FromArgb(96, 165, 250), s);

    // Brightness sun symbol (center-right area)
    DrawSunSymbol(g, 41*s, 38*s, 9*s, Color.FromArgb(250, 204, 21));

    // Monitor stands
    using var standPen = new Pen(Color.FromArgb(148, 163, 184), 2*s);
    // Stand 1
    g.DrawLine(standPen, 23*s, 36*s, 23*s, 42*s);
    g.DrawLine(standPen, 17*s, 42*s, 29*s, 42*s);
    // Stand 2
    g.DrawLine(standPen, 41*s, 34*s, 41*s, 44*s);
    g.DrawLine(standPen, 35*s, 44*s, 47*s, 44*s);

    bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
}

void DrawMonitor(Graphics g, float x, float y, float w, float h, Color bezel, Color screen, float s)
{
    // Bezel (outer rectangle)
    using (var bezelBrush = new SolidBrush(bezel))
    {
        g.FillRectangle(bezelBrush, x, y, w, h);
    }
    // Screen (inner rectangle with slight inset)
    float inset = 2*s;
    using (var screenBrush = new SolidBrush(screen))
    {
        g.FillRectangle(screenBrush, x + inset, y + inset, w - 2*inset, h - 2*inset);
    }
}

void DrawSunSymbol(Graphics g, float cx, float cy, float radius, Color color)
{
    using var brush = new SolidBrush(color);
    using var pen = new Pen(color, 1.5f);

    // Center circle
    g.FillEllipse(brush, cx - radius*0.4f, cy - radius*0.4f, radius*0.8f, radius*0.8f);

    // Rays
    for (int i = 0; i < 8; i++)
    {
        double angle = i * Math.PI / 4;
        float x1 = cx + (float)(Math.Cos(angle) * radius * 0.55);
        float y1 = cy + (float)(Math.Sin(angle) * radius * 0.55);
        float x2 = cx + (float)(Math.Cos(angle) * radius * 0.9);
        float y2 = cy + (float)(Math.Sin(angle) * radius * 0.9);
        g.DrawLine(pen, x1, y1, x2, y2);
    }
}

// Generate multiple sizes
DrawMonitorIcon(16, "icon_16.png");
DrawMonitorIcon(32, "icon_32.png");
DrawMonitorIcon(48, "icon_48.png");
DrawMonitorIcon(64, "icon_64.png");
DrawMonitorIcon(256, "icon_256.png");

Console.WriteLine("PNGs generated. Use an ICO combiner or the IcoWriter below to produce app.ico.");
