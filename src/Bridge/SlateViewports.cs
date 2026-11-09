using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Rhino.Display;
using Rhino.Geometry;
using RhinoWindows.Forms.Controls;

namespace Slate.Bridge;

// A real Rhino viewport inside a Slate tab ("Viewport Tab").
//
// The HTML can't host a native control, so the native ViewportControl sits on
// top of the WebView2, covering the whole pane (tab bar included). The UI
// (ViewportHost.svelte) reports that rect plus the rects of everything in the
// pane that must stay clickable (tab pills, header buttons, corner handles,
// the viewport's own menu chips); those are cut out of the control's shape
// (Control.Region) and the pane is painted the viewport's background colour,
// so the holes read as transparent. While anything has to draw OVER the
// viewport (drags, menus, popups) the UI sends visible:false.
//
// Messages: viewport_rect / viewport_cmd / viewport_remove / viewport_focus in;
// viewport_info (display modes, background) and viewport_state (camera +
// settings, persisted on the tab) out.
public partial class SlateWindow
{
    private readonly Dictionary<string, SlateViewportControl> _viewports = new();
    private bool _viewportRedrawHooked;

    // Custom Preview rows with Focus on (source ids). The orbit pivot is the
    // centre of their combined bounding box, re-read at every drag start, so
    // it follows the geometry without ever moving the camera on a solve.
    private HashSet<string> _focusIds = new();

    // ── messages in ──────────────────────────────────────────────────────────

    private void OnViewportRect(JsonElement root)
    {
        string id    = root.GetProperty("id").GetString() ?? "";
        bool visible = root.GetProperty("visible").GetBoolean();
        double dpr   = root.GetProperty("dpr").GetDouble();
        double x = root.GetProperty("x").GetDouble(), y = root.GetProperty("y").GetDouble();
        double w = root.GetProperty("w").GetDouble(), h = root.GetProperty("h").GetDouble();

        // CSS px → device px. devicePixelRatio already folds in both the
        // monitor's DPI scale and WebView2's own Ctrl+scroll zoom. The WebView
        // is Dock=Fill, so its client origin is the form's client origin.
        var r = new Rectangle(
            (int)Math.Round(x * dpr), (int)Math.Round(y * dpr),
            (int)Math.Round(w * dpr), (int)Math.Round(h * dpr));

        bool isNew = false;
        if (!_viewports.TryGetValue(id, out var vc))
        {
            if (!visible || r.Width <= 0 || r.Height <= 0) return;
            vc = new SlateViewportControl { Pivot = FocusPivot, ReturnFocus = () => _webView.Focus() };
            vc.NavigationEnded += () => PostViewportState(id, vc);
            _viewports[id] = vc;
            Controls.Add(vc);
            vc.BringToFront();
            HookViewportRedraw();
            isNew = true;
        }

        bool resized = vc.Size != r.Size;
        vc.Bounds = r;
        if (isNew)
        {
            root.TryGetProperty("state", out var state);
            InitViewport(vc, state);
            PostViewportInfo(id, vc);
            PostViewportState(id, vc);
        }
        else if (resized) FitFrustumToScreen(vc.Viewport);
        ApplyCuts(vc, root, dpr);

        if (visible && r.Width > 0 && r.Height > 0)
        {
            CancelPendingHide(id);
            vc.Visible = true;
        }
        else if (vc.Visible && !_pendingHides.ContainsKey(id)) BeginSnapshotHide(id, vc);
    }

    // ── hide without the model vanishing ─────────────────────────────────────
    //
    // Hiding the native control (so a menu/popup/drag can draw) would leave an
    // empty pane. Instead: grab what the viewport shows right now, send it to
    // the UI, and only hide once the UI says the picture is in place
    // (viewport_snapshot_shown) — or after a short fallback, so a lost reply
    // can't leave a menu stuck behind the viewport.
    private readonly Dictionary<string, System.Windows.Forms.Timer> _pendingHides = new();

    private void BeginSnapshotHide(string id, SlateViewportControl vc)
    {
        string? data = null;
        try { data = CaptureScreenJpeg(vc); } catch { /* off-screen / covered: just hide */ }
        if (data == null) { vc.Visible = false; return; }

        var fallback = new System.Windows.Forms.Timer { Interval = 250 };
        fallback.Tick += (_, _) => FinishPendingHide(id);
        _pendingHides[id] = fallback;
        fallback.Start();
        PostToJs(JsonSerializer.Serialize(new { type = "viewport_snapshot", id, data }));
    }

    private void OnViewportSnapshotShown(JsonElement root) =>
        FinishPendingHide(root.GetProperty("id").GetString() ?? "");

    private void FinishPendingHide(string id)
    {
        if (!_pendingHides.TryGetValue(id, out var t)) return;
        t.Dispose();
        _pendingHides.Remove(id);
        if (_viewports.TryGetValue(id, out var vc)) vc.Visible = false;
    }

    private void CancelPendingHide(string id)
    {
        if (!_pendingHides.TryGetValue(id, out var t)) return;
        t.Dispose();
        _pendingHides.Remove(id);
    }

    // What's on screen inside the control's bounds (the viewport is on top of
    // everything in this window, so that's the viewport — holes included).
    private static string CaptureScreenJpeg(Control vc)
    {
        var screen = vc.RectangleToScreen(vc.ClientRectangle);
        if (screen.Width <= 0 || screen.Height <= 0) return null!;
        using var bmp = new Bitmap(screen.Width, screen.Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(screen.Location, System.Drawing.Point.Empty, screen.Size);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private void OnViewportRemove(JsonElement root)
    {
        string id = root.GetProperty("id").GetString() ?? "";
        if (!_viewports.TryGetValue(id, out var vc)) return;
        CancelPendingHide(id);
        _viewports.Remove(id);
        Controls.Remove(vc);
        vc.Dispose();
    }

    private void OnViewportCmd(JsonElement root)
    {
        string id  = root.GetProperty("id").GetString() ?? "";
        string cmd = root.GetProperty("cmd").GetString() ?? "";
        if (!_viewports.TryGetValue(id, out var vc)) return;
        var vp = vc.Viewport;
        root.TryGetProperty("value", out var value);

        switch (cmd)
        {
            case "displayMode":
                if (Guid.TryParse(value.GetString(), out var modeId) && DisplayModeDescription.GetDisplayMode(modeId) is { } mode)
                    vp.DisplayMode = mode;
                PostViewportInfo(id, vc);   // background follows the mode
                break;
            case "view":
                SetNamedView(vc, value.GetString() ?? "Perspective");
                vp.ZoomExtents();
                break;
            case "projection":
                if (value.GetString() == "parallel") vp.ChangeToParallelProjection(true);
                else vp.ChangeToPerspectiveProjection(true, 50);
                break;
            case "zoomExtents":
                vp.ZoomExtents();
                break;
            case "zoomFocus":
                var box = FocusBox();
                if (box.IsValid) vp.ZoomBoundingBox(box);
                break;
            case "grid":       vp.ConstructionGridVisible = value.GetBoolean(); break;
            case "cplaneAxes": vp.ConstructionAxesVisible = value.GetBoolean(); break;
            case "worldAxes":  vp.WorldAxesVisible        = value.GetBoolean(); break;
            case "locked":     vc.Locked                  = value.GetBoolean(); break;
        }
        vc.Refresh();
        PostViewportState(id, vc);
    }

    // zoom:true only comes from a click on a row's Focus button: every open
    // viewport then frames what's focused now (nothing → zoom extents). The
    // plain form (load, row deleted…) just updates the orbit pivot.
    private void OnViewportFocus(JsonElement root)
    {
        _focusIds = new HashSet<string>(root.GetProperty("ids").EnumerateArray().Select(e => e.GetString() ?? ""));
        if (!(root.TryGetProperty("zoom", out var z) && z.GetBoolean())) return;

        var box = FocusBox();
        foreach (var kv in _viewports)
        {
            var vp = kv.Value.Viewport;
            if (box.IsValid) vp.ZoomBoundingBox(box);
            else vp.ZoomExtents();
            kv.Value.Refresh();
            PostViewportState(kv.Key, kv.Value);
        }
    }

    // ── setup ────────────────────────────────────────────────────────────────

    // The viewport's own camera — never copied from a Rhino view (copying one
    // carried over that view's frustum aspect and stretched the image). Runs
    // after the first sizing so the frustum is built for the real aspect.
    // `state` is what the tab saved (viewport_state); absent → defaults.
    private static void InitViewport(SlateViewportControl vc, JsonElement state)
    {
        var vp = vc.Viewport;
        bool has = state.ValueKind == JsonValueKind.Object;

        vc.ViewName = has && state.TryGetProperty("view", out var v) ? v.GetString() ?? "Perspective" : "Perspective";
        SetNamedView(vc, vc.ViewName);

        DisplayModeDescription? mode = null;
        if (has && state.TryGetProperty("displayMode", out var dm) && Guid.TryParse(dm.GetString(), out var dmId))
            mode = DisplayModeDescription.GetDisplayMode(dmId);
        mode ??= DisplayModeDescription.GetDisplayModes().FirstOrDefault(d => d.EnglishName == "Shaded");
        if (mode != null) vp.DisplayMode = mode;

        if (has)
        {
            if (state.TryGetProperty("grid", out var g))  vp.ConstructionGridVisible = g.GetBoolean();
            if (state.TryGetProperty("cplaneAxes", out var ca)) vp.ConstructionAxesVisible = ca.GetBoolean();
            if (state.TryGetProperty("worldAxes", out var wa))  vp.WorldAxesVisible = wa.GetBoolean();
            if (state.TryGetProperty("locked", out var lk)) vc.Locked = lk.GetBoolean();
        }

        if (has && state.TryGetProperty("loc", out var loc) && state.TryGetProperty("target", out var tgt))
        {
            bool parallel = state.TryGetProperty("parallel", out var par) && par.GetBoolean();
            double lens   = state.TryGetProperty("lens", out var ln) ? ln.GetDouble() : 50;
            if (parallel) vp.ChangeToParallelProjection(true);
            else vp.ChangeToPerspectiveProjection(true, lens);
            vp.SetCameraLocations(ReadPoint(tgt), ReadPoint(loc));
            if (state.TryGetProperty("up", out var up)) vp.CameraUp = new Vector3d(ReadPoint(up));
            // frustumH is saved as null for perspective views — GetDouble on it
            // threw here, skipping everything after (viewport_info → no pane
            // background, square cut-outs; no viewport_state → empty chips).
            double? height = state.TryGetProperty("frustumH", out var fh) && fh.ValueKind == JsonValueKind.Number ? fh.GetDouble() : null;
            FitFrustumToScreen(vp, height);
        }
        else
        {
            FitFrustumToScreen(vp);
            vp.ZoomExtents();
        }
        vc.Invalidate();
    }

    private static void SetNamedView(SlateViewportControl vc, string name)
    {
        var proj = name switch
        {
            "Top"       => DefinedViewportProjection.Top,
            "Bottom"    => DefinedViewportProjection.Bottom,
            "Front"     => DefinedViewportProjection.Front,
            "Back"      => DefinedViewportProjection.Back,
            "Left"      => DefinedViewportProjection.Left,
            "Right"     => DefinedViewportProjection.Right,
            "Two-Point" => DefinedViewportProjection.TwoPointPerspective,
            _           => DefinedViewportProjection.Perspective,
        };
        vc.Viewport.SetProjection(proj, name, false);
        if (proj == DefinedViewportProjection.Perspective || proj == DefinedViewportProjection.TwoPointPerspective)
            vc.Viewport.Camera35mmLensLength = 50;
        vc.ViewName = name;
        FitFrustumToScreen(vc.Viewport);
    }

    // Rebuild a symmetric frustum for the control's current aspect, keeping
    // the lens. Without this a resize keeps the old aspect → stretched image.
    // ChangeTo*Projection can also move the camera, so location/target/up are
    // put back afterwards; parallel views keep their zoom (frustum height),
    // `parallelHeight` overriding it on restore.
    private static void FitFrustumToScreen(RhinoViewport vp, double? parallelHeight = null)
    {
        var loc = vp.CameraLocation; var tgt = vp.CameraTarget; var up = vp.CameraUp;
        double height = parallelHeight ?? FrustumHeight(vp);

        if (vp.IsPerspectiveProjection) vp.ChangeToPerspectiveProjection(true, vp.Camera35mmLensLength);
        else if (vp.IsParallelProjection) vp.ChangeToParallelProjection(true);
        else return;

        vp.SetCameraLocations(tgt, loc);
        vp.CameraUp = up;

        if (vp.IsParallelProjection && height > 0 && vp.GetFrustum(out _, out _, out _, out _, out double n, out double f))
        {
            double halfH = height / 2, halfW = halfH * vp.ScreenPortAspect;
            var vi = new Rhino.DocObjects.ViewportInfo(vp);
            vi.SetFrustum(-halfW, halfW, -halfH, halfH, n, f);
            vp.SetViewProjection(vi, false);
        }
    }

    private static double FrustumHeight(RhinoViewport vp) =>
        vp.GetFrustum(out _, out _, out double b, out double t, out _, out _) ? t - b : 0;

    // ── focus ────────────────────────────────────────────────────────────────

    private BoundingBox FocusBox()
    {
        var box = BoundingBox.Empty;
        foreach (var id in _focusIds)
            if (_customPreviews.TryGetValue(id, out var cp) && !cp.Hidden)
            {
                var b = cp.ClippingBox;
                if (b.IsValid) box.Union(b);
            }
        return box;
    }

    // Orbit pivot: the focused previews' centre, else the camera target.
    private Point3d FocusPivot(RhinoViewport vp)
    {
        var box = FocusBox();
        return box.IsValid ? box.Center : vp.CameraTarget;
    }

    // ── messages out ─────────────────────────────────────────────────────────

    private void PostViewportInfo(string id, SlateViewportControl vc)
    {
        var modes = DisplayModeDescription.GetDisplayModes()
            .Where(d => d.InMenu)
            .Select(d => new { id = d.Id.ToString(), name = d.LocalName })
            .ToArray();
        PostToJs(JsonSerializer.Serialize(new { type = "viewport_info", id, modes, bg = BackgroundCss(vc.Viewport) }));
    }

    private void PostViewportState(string id, SlateViewportControl vc)
    {
        var vp = vc.Viewport;
        var loc = vp.CameraLocation; var tgt = vp.CameraTarget;
        var state = new
        {
            view        = vc.ViewName,
            displayMode = vp.DisplayMode?.Id.ToString(),
            displayName = vp.DisplayMode?.LocalName,
            parallel    = vp.IsParallelProjection,
            lens        = vp.Camera35mmLensLength,
            loc         = new[] { loc.X, loc.Y, loc.Z },
            up          = new[] { vp.CameraUp.X, vp.CameraUp.Y, vp.CameraUp.Z },
            frustumH    = vp.IsParallelProjection ? FrustumHeight(vp) : (double?)null,
            target      = new[] { tgt.X, tgt.Y, tgt.Z },
            grid        = vp.ConstructionGridVisible,
            cplaneAxes  = vp.ConstructionAxesVisible,
            worldAxes   = vp.WorldAxesVisible,
            locked      = vc.Locked,
        };
        PostToJs(JsonSerializer.Serialize(new { type = "viewport_state", id, state }));
    }

    // CSS background matching what the viewport paints behind the model, for
    // the holes cut into it. Gradients: Rhino's 2-colour fill is top→bottom;
    // the 4-colour one is approximated with its first two colours.
    private static string BackgroundCss(RhinoViewport vp)
    {
        var attrs = vp.DisplayMode?.DisplayAttributes;
        Color top, bottom;
        switch (attrs?.FillMode)
        {
            case DisplayPipelineAttributes.FrameBufferFillMode.SolidColor:
                attrs.GetFill(out top, out _, out _, out _);
                return Hex(top);
            case DisplayPipelineAttributes.FrameBufferFillMode.Gradient2Color:
            case DisplayPipelineAttributes.FrameBufferFillMode.Gradient4Color:
                attrs.GetFill(out top, out bottom, out _, out _);
                return $"linear-gradient({Hex(top)}, {Hex(bottom)})";
            case DisplayPipelineAttributes.FrameBufferFillMode.Renderer:
                var rs = Rhino.RhinoDoc.ActiveDoc?.RenderSettings;
                if (rs != null)
                    return rs.BackgroundStyle == Rhino.Display.BackgroundStyle.Gradient
                        ? $"linear-gradient({Hex(rs.BackgroundColorTop)}, {Hex(rs.BackgroundColorBottom)})"
                        : Hex(rs.BackgroundColorTop);
                break;
        }
        return Hex(Rhino.ApplicationSettings.AppearanceSettings.ViewportBackgroundColor);
    }

    private static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static Point3d ReadPoint(JsonElement a) =>
        new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());

    // ── shape ────────────────────────────────────────────────────────────────

    // Holes for the pane's clickable bits (see ViewportHost.svelte cutRects):
    // the HTML underneath shows through and gets the clicks.
    private static void ApplyCuts(SlateViewportControl vc, JsonElement root, double dpr)
    {
        var old = vc.Region;
        Region? region = null;
        if (root.TryGetProperty("cuts", out var cuts) && cuts.GetArrayLength() > 0)
        {
            region = new Region(new Rectangle(0, 0, vc.Width, vc.Height));
            foreach (var c in cuts.EnumerateArray())
            {
                // Floor the origin, ceil the far edge: a half-pixel sliver of
                // viewport left over a button would still swallow its clicks.
                double cx = c.GetProperty("x").GetDouble(), cy = c.GetProperty("y").GetDouble();
                double cw = c.GetProperty("w").GetDouble(), ch = c.GetProperty("h").GetDouble();
                int x0 = (int)Math.Floor(cx * dpr), y0 = (int)Math.Floor(cy * dpr);
                int x1 = (int)Math.Ceiling((cx + cw) * dpr), y1 = (int)Math.Ceiling((cy + ch) * dpr);
                region.Exclude(new Rectangle(x0, y0, x1 - x0, y1 - y0));
            }
        }
        vc.Region = region;
        old?.Dispose();
    }

    // ── redraw ───────────────────────────────────────────────────────────────

    // GH redraws Rhino's own views after a solve / preview change, but not
    // controls it doesn't know about. Piggyback: whenever any OTHER viewport
    // draws, repaint ours.
    //
    // Not Invalidate(): WM_PAINT is only served once the message queue is
    // empty, and during a slider drag it never is (solves every ~11 ms), so
    // ours skipped about 2 of 3 solves and moved in jumps (measured
    // 2026-11-09: 705 solves, Rhino's view drew 705, ours ~230). A posted
    // Refresh runs in order, right after the current draw; the flag folds a
    // burst (several Rhino views drawing) into one repaint.
    private bool _viewportRefreshQueued;

    private void HookViewportRedraw()
    {
        if (_viewportRedrawHooked) return;
        _viewportRedrawHooked = true;
        DisplayPipeline.DrawForeground += OnAnyViewportDrawForeground;
    }

    private void OnAnyViewportDrawForeground(object sender, DrawEventArgs e)
    {
        if (_viewports.Count == 0 || IsDisposed) return;
        var drawnId = e.Viewport.Id;
        foreach (var vc in _viewports.Values)
            if (vc.Viewport.Id == drawnId) return;   // our own repaint — don't loop
        if (_viewportRefreshQueued || !IsHandleCreated) return;
        _viewportRefreshQueued = true;
        BeginInvoke(new Action(() =>
        {
            _viewportRefreshQueued = false;
            foreach (var vc in _viewports.Values)
                if (vc.Visible && !vc.IsDisposed) vc.Refresh();
        }));
    }

    private void DisposeViewports()
    {
        if (_viewportRedrawHooked) DisplayPipeline.DrawForeground -= OnAnyViewportDrawForeground;
        _viewportRedrawHooked = false;
        foreach (var vc in _viewports.Values) vc.Dispose();
        _viewports.Clear();
    }
}

// ViewportControl with Rhino-style mouse navigation instead of the control's
// own (it navigates in its OnMouseMove/OnMouseWheel overrides; we replace
// both and never call base):
//   perspective: RMB orbit · Shift+RMB pan · Ctrl+RMB dolly
//   parallel:    RMB pan   · Ctrl+Shift+RMB orbit · Ctrl+RMB dolly
//   wheel: zoom at the cursor
// Orbit turns the camera around Pivot (the focus centre), not the camera
// target, so changing focus never jumps the view.
public class SlateViewportControl : ViewportControl
{
    public bool Locked;
    public string ViewName = "Perspective";
    public Func<RhinoViewport, Point3d>? Pivot;
    public event Action? NavigationEnded;
    // Called on mouse up. The control takes focus for the drag, but it has no
    // keyboard use of its own; left focused it swallowed every key meant for
    // the UI (Tab = edit mode, Ctrl+S, Delete…), so focus goes back to the WebView.
    public Action? ReturnFocus;

    private System.Drawing.Point _prev;
    private Point3d _pivot;
    private readonly System.Windows.Forms.Timer _wheelEnd = new() { Interval = 300 };

    // Radians of orbit per pixel dragged.
    private const double OrbitSpeed = 0.008;

    public SlateViewportControl()
    {
        _wheelEnd.Tick += (_, _) => { _wheelEnd.Stop(); NavigationEnded?.Invoke(); };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _prev  = e.Location;
        _pivot = Pivot?.Invoke(Viewport) ?? Viewport.CameraTarget;
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Locked || e.Button != MouseButtons.Right) { _prev = e.Location; return; }

        var vp    = Viewport;
        var mods  = ModifierKeys;
        bool ctrl = (mods & Keys.Control) != 0, shift = (mods & Keys.Shift) != 0;

        if (ctrl && !shift)                                    vp.MouseInOutDolly(_prev, e.Location);
        else if (vp.IsParallelProjection ? (ctrl && shift) : !shift) Orbit(e.Location);
        else                                                   vp.MouseLateralDolly(_prev, e.Location);

        _prev = e.Location;
        Refresh();
    }

    private void Orbit(System.Drawing.Point p)
    {
        var vp = Viewport;
        double dx = p.X - _prev.X, dy = p.Y - _prev.Y;
        if (dx != 0) vp.Rotate(-dx * OrbitSpeed, Vector3d.ZAxis, _pivot);
        if (dy != 0)
        {
            var right = Vector3d.CrossProduct(vp.CameraDirection, vp.CameraUp);
            if (right.Unitize()) vp.Rotate(-dy * OrbitSpeed, right, _pivot);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right && !Locked) NavigationEnded?.Invoke();
        ReturnFocus?.Invoke();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Locked || e.Delta == 0) return;
        Viewport.Magnify(e.Delta > 0 ? 1.15 : 1 / 1.15, false, e.Location);
        Refresh();
        _wheelEnd.Stop(); _wheelEnd.Start();   // save the camera once the wheel settles
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _wheelEnd.Dispose();
        base.Dispose(disposing);
    }
}
