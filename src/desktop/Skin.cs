using System.Drawing.Drawing2D;

namespace Spanda;

/// <summary>4 Switchable Hardware Palettes for the Spanda // SadNap Tactile Desktop Radar Deck.</summary>
static class Skin
{
    public sealed record Palette(
        string Name,
        Color Body,
        Color BodyLight,
        Color BodyDark,
        Color Lcd,
        Color Glow,
        Color GlowDim,
        Color GlowShadow,
        Color Accent,
        Color TearCyan,
        Color Red,
        Color Selection,
        Color LabelText,
        Color BrightText
    );

    public static readonly Palette[] Palettes =
    [
        // 0: Spanda Cyber-Neon (Default: Obsidian + Electric Cyan Tears + Hot Magenta Accent)
        new(
            "SPANDA CYBER",
            Color.FromArgb(0x10, 0x12, 0x20),
            Color.FromArgb(0x2D, 0x34, 0x5E),
            Color.FromArgb(0x07, 0x08, 0x10),
            Color.FromArgb(0x06, 0x08, 0x13),
            Color.FromArgb(0x00, 0xF5, 0xD4),
            Color.FromArgb(0x00, 0x7A, 0x88),
            Color.FromArgb(0x05, 0x29, 0x36),
            Color.FromArgb(0xFF, 0x2A, 0x85),
            Color.FromArgb(0x00, 0xF5, 0xD4),
            Color.FromArgb(0xFF, 0x38, 0x64),
            Color.FromArgb(0x24, 0x12, 0x42),
            Color.FromArgb(0x9D, 0xAD, 0xD8),
            Color.FromArgb(0xF0, 0xF6, 0xFF)
        ),
        // 1: Nixie Filament (Warm incandescent vacuum-tube orange on smoky dark brown)
        new(
            "NIXIE FILAMENT",
            Color.FromArgb(0x22, 0x18, 0x12),
            Color.FromArgb(0x4E, 0x36, 0x27),
            Color.FromArgb(0x0F, 0x09, 0x06),
            Color.FromArgb(0x12, 0x08, 0x04),
            Color.FromArgb(0xFF, 0x9F, 0x3A),
            Color.FromArgb(0x8A, 0x4B, 0x18),
            Color.FromArgb(0x3D, 0x1A, 0x06),
            Color.FromArgb(0xFF, 0xD9, 0x8A),
            Color.FromArgb(0x55, 0xE6, 0xFF),
            Color.FromArgb(0xFF, 0x55, 0x3D),
            Color.FromArgb(0x5C, 0x2C, 0x10),
            Color.FromArgb(0xD6, 0xB4, 0x96),
            Color.FromArgb(0xF6, 0xE6, 0xD6)
        ),
        // 2: Tektronix CRT Phosphor (Vintage P31 green oscilloscope radar on gunmetal slate)
        new(
            "TEKTRONIX CRT",
            Color.FromArgb(0x13, 0x1D, 0x1A),
            Color.FromArgb(0x2A, 0x42, 0x3A),
            Color.FromArgb(0x08, 0x0E, 0x0C),
            Color.FromArgb(0x05, 0x11, 0x0B),
            Color.FromArgb(0x00, 0xFF, 0x88),
            Color.FromArgb(0x12, 0x7A, 0x48),
            Color.FromArgb(0x06, 0x33, 0x1D),
            Color.FromArgb(0xFE, 0xE4, 0x40),
            Color.FromArgb(0x00, 0xF5, 0xD4),
            Color.FromArgb(0xFF, 0x4D, 0x6D),
            Color.FromArgb(0x0E, 0x47, 0x2B),
            Color.FromArgb(0x8E, 0xC9, 0xB0),
            Color.FromArgb(0xE2, 0xFA, 0xEE)
        ),
        // 3: Bazzite Vapor (Handheld gaming purple + electric gold on carbon violet)
        new(
            "BAZZITE VAPOR",
            Color.FromArgb(0x1C, 0x13, 0x26),
            Color.FromArgb(0x43, 0x2C, 0x5E),
            Color.FromArgb(0x0C, 0x07, 0x12),
            Color.FromArgb(0x0F, 0x08, 0x18),
            Color.FromArgb(0xC7, 0x7D, 0xFF),
            Color.FromArgb(0x6C, 0x3B, 0x9E),
            Color.FromArgb(0x2A, 0x12, 0x45),
            Color.FromArgb(0xFE, 0xE4, 0x40),
            Color.FromArgb(0x00, 0xF5, 0xD4),
            Color.FromArgb(0xFF, 0x5D, 0x8F),
            Color.FromArgb(0x3C, 0x1E, 0x5E),
            Color.FromArgb(0xC8, 0xB6, 0xE2),
            Color.FromArgb(0xF8, 0xF0, 0xFF)
        ),
    ];

    public static int CurrentPaletteIndex { get; private set; }
    public static Palette Current => Palettes[CurrentPaletteIndex];

    public static string CyclePalette()
    {
        CurrentPaletteIndex = (CurrentPaletteIndex + 1) % Palettes.Length;
        return Current.Name;
    }

    public static Color Body => Current.Body;
    public static Color BodyLight => Current.BodyLight;
    public static Color BodyDark => Current.BodyDark;
    public static Color Lcd => Current.Lcd;
    public static Color Glow => Current.Glow;
    public static Color GlowDim => Current.GlowDim;
    public static Color GlowShadow => Current.GlowShadow;
    public static Color Accent => Current.Accent;
    public static Color TearCyan => Current.TearCyan;
    public static Color Red => Current.Red;
    public static Color Selection => Current.Selection;
    public static Color LabelText => Current.LabelText;
    public static Color BrightText => Current.BrightText;

    public static readonly Font LcdLarge = new("Consolas", 12.5f, FontStyle.Bold);
    public static readonly Font LcdSmall = new("Consolas", 9f, FontStyle.Bold);
    public static readonly Font LcdTiny = new("Consolas", 7.5f, FontStyle.Bold);
    public static readonly Font Label = new("Segoe UI", 7.5f, FontStyle.Bold);
    public static readonly Font Hint = new("Segoe UI", 8f);
    public static readonly Font Guide = new("Consolas", 9f);
    public static readonly Font Title = new("Segoe UI Semibold", 9.5f);
    public static readonly Font ButtonLarge = new("Segoe UI", 10f, FontStyle.Bold);

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Well(Graphics g, Rectangle screen, float scale)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = RectangleF.Inflate(screen, 3 * scale, 3 * scale);
        using (var path = RoundRect(outer, 5 * scale))
        using (var fill = new SolidBrush(BodyDark))
        using (var edge = new Pen(Color.FromArgb(100, BodyLight), Math.Max(1, scale)))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }
        g.SmoothingMode = mode;
    }

    public static void Scanlines(Graphics g, Rectangle r)
    {
        using var pen = new Pen(Color.FromArgb(46, 0, 0, 0));
        for (var y = r.Top; y < r.Bottom; y += 2)
            g.DrawLine(pen, r.Left, y, r.Right, y);
    }

    public static float Scale(Control c) => c.DeviceDpi / 96f;
}
