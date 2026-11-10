using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Slate.Bridge;

// ── startup splash ───────────────────────────────────────────────────────────
//
// Opening a .gh shows Slate from inside GH's first solve (SlatePanel's
// SolveInstance → EnsureVisible), and the UI thread stays busy until that solve
// ends: the window can't repaint and WebView2 can't even start loading, so it
// used to sit black. Instead: the Slate logo with "Slate" under it, in the
// theme's colours and the user's accent.
//   1. Native: this panel, painted ONCE synchronously right at Show (see
//      EnsureVisible), since nothing can repaint while the thread is blocked.
//   2. HTML: index.html draws the same picture at the same place. This panel
//      is removed once the page has loaded, and the page fades its copy out
//      when the UI is ready (App.svelte).
// Geometry is in CSS px and must match #splash in ui/index.html.
// (An animated version — sliders gliding — was tried first and dropped,
// 2026-11-09: it barely showed and didn't read as working.)
public partial class SlateWindow
{
    private Panel? _splash;

    // CSS px, mirrored in ui/index.html
    private const float SplashLogo = 56f, SplashGap = 10f, SplashText = 14f, SplashBlockH = 56f + 10f + 18f;

    private void CreateSplash()
    {
        _splash = new DoubleBufferedPanel { Dock = DockStyle.Fill };
        _splash.Paint += PaintSplash;
        Controls.Add(_splash);
        _splash.BringToFront();
    }

    // Same colours as the window icon (ApplyWindowIcon).
    private (Color bg, Color text, Color outline, Color fill) SplashColours()
    {
        bool dark = _lastAppliedTheme != "light";
        return (
            dark ? Color.FromArgb(26, 26, 26)    : Color.FromArgb(228, 225, 216),
            dark ? Color.FromArgb(239, 239, 239) : Color.FromArgb(42, 39, 36),
            dark ? Color.FromArgb(0xe8, 0xeb, 0xf0) : Color.FromArgb(0x20, 0x1e, 0x1d),
            _accentIconFill ?? (dark ? Color.FromArgb(0x74, 0xa2, 0xff) : Color.FromArgb(0x5b, 0x8e, 0xf5)));
    }

    private void PaintSplash(object? sender, PaintEventArgs e)
    {
        var (bg, text, outline, fill) = SplashColours();
        var g = e.Graphics;
        g.Clear(bg);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        float s = DeviceDpi / 96f;
        var size = _splash!.ClientSize;
        float top  = size.Height / 2f - SplashBlockH * s / 2f;
        float logo = SplashLogo * s;
        SlateLogo.Draw(g, new RectangleF(size.Width / 2f - logo / 2f, top, logo, logo), outline, fill);

        using var font  = new Font("Segoe UI", SplashText * s, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(166, text));   // ~65%
        using var fmt   = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString("Slate", font, brush, new RectangleF(0, top + logo + SplashGap * s, size.Width, SplashText * s * 2), fmt);
    }

    // Page loaded (its own copy of the splash is on screen now).
    private void RemoveSplash()
    {
        if (_splash == null) return;
        Controls.Remove(_splash);
        _splash.Dispose();
        _splash = null;
    }

    // Query string for index.html's splash: theme + accent, known before any
    // app script or stylesheet runs there.
    private string SplashQuery()
    {
        var (_, _, _, fill) = SplashColours();
        return $"?theme={(_lastAppliedTheme == "light" ? "light" : "dark")}&accent=%23{fill.R:x2}{fill.G:x2}{fill.B:x2}";
    }

    // The accent, like the theme (theme.txt), is only known once the UI has
    // loaded — remembered across sessions so the splash and the window icon
    // use it from the very first frame.
    static readonly string _accentDefaultFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slate", "accent.txt");

    static Color? ReadAccentDefaultFile()
    {
        try { return ColorTranslator.FromHtml(File.ReadAllText(_accentDefaultFile).Trim()); }
        catch { return null; }
    }

    static void WriteAccentDefaultFile(string hex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_accentDefaultFile)!);
            File.WriteAllText(_accentDefaultFile, hex);
        }
        catch { /* best-effort */ }
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}
