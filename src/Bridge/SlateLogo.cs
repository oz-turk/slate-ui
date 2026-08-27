using System.Drawing;
using System.Drawing.Drawing2D;

namespace Slate.Bridge;

// Shared brand mark: two rounded squares, offset diagonally (outline
// upper-left... actually lower-left, fill upper-right), per the "Tur 3 / 1A"
// design (Slate Logo.dc.html). Proportions are normalized against the
// design's 64px reference frame so the same helper draws crisply at
// GH-canvas size (24px) and titlebar-icon size (32px).
internal static class SlateLogo
{
    // Fractions of the target square size, taken from the 64px reference:
    // outline square at (5.12, 17.3) size 38.64 radius 8.4; fill square at
    // (20.24, 8.06) size 38.64 radius 8.4. This is the original edge-to-edge
    // composition (outline at (0, 14.5), fill at (18, 3.5), size 46 — already
    // vertically centered, see git history) scaled 0.84x about the frame's own
    // center (32, 32) to leave a visible margin on every side (~8% horizontal,
    // ~12.6% vertical — the mark's natural bbox is wider than tall, so equal
    // scaling doesn't give equal margins on both axes). Previously the mark
    // ran flush to the left/right edges, which reads as cramped next to other
    // plugin icons (e.g. Nautilus, ~5-9% margin) once placed in a standalone
    // square frame like a food4rhino/store icon rather than a toolbar slot.
    const float OutlineX = 5.12f  / 64f, OutlineY = 17.3f / 64f;
    const float FillX    = 20.24f / 64f, FillY    = 8.06f / 64f;
    const float SquareSz = 38.64f / 64f;
    const float Radius   = 8.4f   / 64f;
    const float StrokeFr = 2.52f  / 64f;

    public static void Draw(Graphics g, RectangleF bounds, Color outline, Color fill, float scale = 1f)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (scale != 1f)
        {
            float inset = bounds.Width * (1f - scale) / 2f;
            bounds = new RectangleF(bounds.X + inset, bounds.Y + inset, bounds.Width - inset * 2, bounds.Height - inset * 2);
        }
        float size = bounds.Width;

        float sq     = size * SquareSz;
        float radius = size * Radius;
        float stroke = Math.Max(1.5f, size * StrokeFr);

        // Outline first, fill on top — matches the original design's DOM
        // order (outline div before the filled div), where the blue square
        // sits in front and visibly overlaps the outline's corner.
        var outlineRect = new RectangleF(
            bounds.X + size * OutlineX + stroke / 2,
            bounds.Y + size * OutlineY + stroke / 2,
            sq - stroke, sq - stroke);
        using (var path = RoundedRect(outlineRect, radius - stroke / 2))
        using (var pen = new Pen(outline, stroke))
            g.DrawPath(pen, path);

        var fillRect = new RectangleF(bounds.X + size * FillX, bounds.Y + size * FillY, sq, sq);
        using (var path = RoundedRect(fillRect, radius))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        radius = Math.Max(0, radius);
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // One-time GDI handle for the process lifetime — built once and cached
    // by the caller, not worth the DestroyIcon P/Invoke.
    public static Icon ToIcon(int size, Color outline, Color fill)
    {
        using var bmp = ToBitmap(size, outline, fill);
        return Icon.FromHandle(bmp.GetHicon());
    }

    // Light-background pairing (dark outline, blue fill) — used wherever
    // the mark sits on GH's own light canvas/ribbon chrome: the component
    // icon (SlatePanel.Icon) and the category tab icon (SlateAssemblyPriority).
    public static readonly Color CanvasOutline = Color.FromArgb(0x16, 0x18, 0x1c);
    public static readonly Color CanvasFill    = Color.FromArgb(0x5b, 0x8e, 0xf5);

    public static Bitmap ToBitmap(int size, Color outline, Color fill, float scale = 1f)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        Draw(g, new RectangleF(0, 0, size, size), outline, fill, scale);
        return bmp;
    }
}
