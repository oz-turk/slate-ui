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
// Rhino draws straight into the control, with a reset after every paint that
// keeps it from crashing (RenderTargetReset). Where that reset isn't
// available the picture comes from a hidden floating Rhino view instead,
// captured to a bitmap on every paint (see SlateViewportControl.CaptureMode).
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
            // Device px in the WebView's client space (it is Dock=Fill, same
            // origin as vc's parent); JS divides by devicePixelRatio.
            vc.HoldPaint = () =>
            {
                if (!HoldViewportPaint()) return false;
                _viewportPaintHeld = true;
                return true;
            };
            // The Rhino document was replaced and took the hidden view with
            // it: the new one starts from the defaults, and the UI is told.
            vc.ViewRecreated += () =>
            {
                InitViewport(vc, default);
                PostViewportInfo(id, vc);
                PostViewportState(id, vc);
            };
            vc.PointerDown += () => PostToJs(JsonSerializer.Serialize(new { type = "viewport_pointerdown", id }));
            vc.ContextClick += p => PostToJs(JsonSerializer.Serialize(new { type = "viewport_context", id, x = vc.Left + p.X, y = vc.Top + p.Y }));
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
            PostViewportState(id, vc, initial: true);
        }
        else if (resized) FitFrustumToScreen(vc.Cam);
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
        var vp = vc.Cam;
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
            case "zoomAtCursor": vc.ZoomAtCursor          = value.GetBoolean(); break;
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
        ZoomViewportsToFocus(extentsIfNone: true);
    }

    // Frames what's focused in every open viewport (view direction kept).
    // Besides the Focus button, a scene apply ends here once its solve is done:
    // a scene can build a model of a very different size or place, and the old
    // framing would leave it off-screen. Ordinary solves (slider drags) never
    // move the camera.
    private void ZoomViewportsToFocus(bool extentsIfNone)
    {
        var box = FocusBox();
        if (!box.IsValid && !extentsIfNone) return;
        foreach (var kv in _viewports)
        {
            var vp = kv.Value.Cam;
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
        var vp = vc.Cam;
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
            if (state.TryGetProperty("zoomAtCursor", out var zc)) vc.ZoomAtCursor = zc.GetBoolean();
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
        // The hidden view lives in the Rhino document: its name keeps the
        // prefix so it never doubles one of Rhino's own ("Perspective", "Top").
        vc.Cam.SetProjection(proj, SlateViewportControl.CaptureMode ? SlateViewportControl.ViewPrefix + name : name, false);
        if (proj == DefinedViewportProjection.Perspective || proj == DefinedViewportProjection.TwoPointPerspective)
            vc.Cam.Camera35mmLensLength = 50;
        vc.ViewName = name;
        FitFrustumToScreen(vc.Cam);
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
        PostToJs(JsonSerializer.Serialize(new { type = "viewport_info", id, modes, bg = BackgroundCss(vc.Cam) }));
    }

    // initial: the report right after the viewport is created from the saved
    // state — a load, not an edit, so JS stores it without marking the .gh
    // modified (a field added to the state later, e.g. zoomAtCursor, would
    // otherwise differ from every older file and show `*` on open).
    private void PostViewportState(string id, SlateViewportControl vc, bool initial = false)
    {
        var vp = vc.Cam;
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
            zoomAtCursor = vc.ZoomAtCursor,
        };
        PostToJs(JsonSerializer.Serialize(new { type = "viewport_state", id, state, initial }));
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
        // Our own repaint (a capture draws the hidden view) — don't loop.
        if (SlateViewportControl.Capturing) return;
        var drawnId = e.Viewport.Id;
        foreach (var vc in _viewports.Values)
            if (vc.CamId == drawnId) return;
        if (_viewportRefreshQueued || !IsHandleCreated) return;
        _viewportRefreshQueued = true;
        BeginInvoke(new Action(() =>
        {
            _viewportRefreshQueued = false;
            if (HoldViewportPaint()) { _viewportPaintHeld = true; return; }
            foreach (var vc in _viewports.Values)
                if (vc.Visible && !vc.IsDisposed) vc.Refresh();
        }));
    }

    // ── no half-solved frames ────────────────────────────────────────────────
    //
    // Between a change (GH expires the downstream components and drops their
    // previews) and the end of the solve, a repaint shows the model with those
    // parts missing — e.g. the bridge vanished for a moment when only the
    // labels input changed, because the bridge sits downstream of it. Rhino's
    // own views never draw in that gap; ours did (piggyback refresh above, or
    // WM_PAINT after a menu closed over it). So while GH is mid-solve ours
    // keeps its last frame and repaints once at SolutionEnd.
    // Safety cap: an object that stays expired for good (nothing re-solves
    // it) must not freeze the view, so a hold never lasts longer than this.
    private const int MaxPaintHoldMs = 1500;
    private bool _viewportPaintHeld;
    private DateTime _paintHoldSince = DateTime.MinValue;

    private bool HoldViewportPaint()
    {
        bool mid = GhMidSolve();
        if (!mid) { _paintHoldSince = DateTime.MinValue; return false; }
        if (_paintHoldSince == DateTime.MinValue) _paintHoldSince = DateTime.UtcNow;
        return (DateTime.UtcNow - _paintHoldSince).TotalMilliseconds < MaxPaintHoldMs;
    }

    // Expired-but-unsolved objects (Phase Blank), or a solve in progress.
    private bool GhMidSolve()
    {
        var doc = HostDocument ?? Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null || !Grasshopper.Kernel.GH_Document.EnableSolutions) return false;   // static: GH's global solver switch
        if (doc.SolutionState == Grasshopper.Kernel.GH_ProcessStep.Process) return true;
        foreach (var o in doc.Objects)
            if (o is Grasshopper.Kernel.IGH_ActiveObject a && !a.Locked
                && a.Phase == Grasshopper.Kernel.GH_SolutionPhase.Blank) return true;
        return false;
    }

    // SolutionEnd (via OnDocSolutionEnd): repaint what was held, unless the
    // next solve is already queued — then its own SolutionEnd does it.
    internal void FlushHeldViewportPaint()
    {
        if (!_viewportPaintHeld || HoldViewportPaint()) return;
        _viewportPaintHeld = false;
        foreach (var vc in _viewports.Values)
            if (vc.Visible && !vc.IsDisposed) vc.Refresh();
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
    // Wheel zoom: false (default) = toward the orbit pivot (focused previews'
    // centre, else the camera target), so the object stays put on screen
    // wherever the cursor is; true = toward the cursor, as Rhino does.
    public bool ZoomAtCursor = false;
    public string ViewName = "Perspective";
    public Func<RhinoViewport, Point3d>? Pivot;
    public event Action? NavigationEnded;
    // Called on mouse up. The control takes focus for the drag, but it has no
    // keyboard use of its own; left focused it swallowed every key meant for
    // the UI (Tab = edit mode, Ctrl+S, Delete…), so focus goes back to the WebView.
    public Action? ReturnFocus;
    // Right-click without drag (Rhino's own click-vs-drag split): opens the
    // pane menu instead of navigating. Control-local point.
    public event Action<System.Drawing.Point>? ContextClick;
    // Any press. Menus/popups stay open over a live viewport (cut out of it),
    // and a click on the native control never reaches the WebView, so JS is
    // told to treat it as a click outside them.
    public event Action? PointerDown;
    // True = GH is mid-solve: skip this paint and keep the last frame (the
    // window repaints at SolutionEnd — see SlateWindow.HoldViewportPaint).
    public Func<bool>? HoldPaint;

    // ── where the picture comes from ─────────────────────────────────────────
    //
    // false: Rhino draws straight into this control (ViewportControl.OnPaint).
    // Left alone that crashes Rhino (8.34 and 9 WIP): the display pipeline
    // keeps a temporary MFC window object for our HWND, MFC deletes it on
    // idle, and a later draw reads freed memory (AccessViolation in
    // CRhinoDisplayPipeline::GetWnd). So the pipeline's window is cleared
    // after every paint (RenderTargetReset) and the next paint sets it anew.
    // true: fallback where that reset can't be found — a hidden floating
    // Rhino view is captured to a bitmap and the bitmap is painted here.
    // No crash (Rhino's own views don't go through that path), but a frame
    // costs about 3x as much (20 ms against 6).
    internal static readonly bool CaptureMode = !RenderTargetReset.Available;

    // Name prefix of the hidden views in the Rhino document.
    internal const string ViewPrefix = "slate_";

    // A capture is drawing one of the hidden views right now.
    internal static bool Capturing;

    // Hidden views that belong to a living control (ActiveViewportID).
    private static readonly HashSet<Guid> s_liveViews = new();

    private RhinoView? _view;
    private IntPtr _viewHwnd;
    private Guid _viewportId;
    private bool _hadView;
    private Bitmap? _frame;

    // The Rhino document was replaced, so the hidden view was made anew and
    // has lost its camera and settings.
    public event Action? ViewRecreated;

    // The viewport that is navigated and drawn: the hidden view's in capture
    // mode, the control's own otherwise.
    public RhinoViewport Cam => (CaptureMode ? EnsureView()?.ActiveViewport : null) ?? Viewport;

    // Cam's id without creating anything (safe inside Rhino's draw events).
    public Guid CamId => CaptureMode ? _viewportId : Viewport.Id;

    private RhinoView? EnsureView()
    {
        if (_view != null && IsWindow(_viewHwnd)) return _view;
        if (IsDisposed) return null;
        ForgetView();
        var doc = Rhino.RhinoDoc.ActiveDoc;
        if (doc == null) return null;

        bool wasModified = doc.Modified;
        var active = doc.Views.ActiveView;
        // Left over from an earlier session (saved into the .3dm with the document).
        foreach (var old in doc.Views.GetViewList(true, false))
            if (old.ActiveViewport.Name.StartsWith(ViewPrefix) && !s_liveViews.Contains(old.ActiveViewportID)) old.Close();

        var size = ClientSize.IsEmpty ? new Size(10, 10) : ClientSize;
        // Created far off-screen and hidden at once, so it never shows.
        var view = doc.Views.Add(ViewPrefix + ViewName, DefinedViewportProjection.Perspective,
            new Rectangle(-32000, -32000, size.Width, size.Height), true);
        if (view == null) return null;
        ShowWindow(GetAncestor(view.Handle, GA_ROOT), SW_HIDE);
        if (active != null && doc.Views.ActiveView?.ActiveViewportID != active.ActiveViewportID) doc.Views.ActiveView = active;
        SizeView(view, size);
        // Adding a view marks the Rhino document modified; that's ours, not the user's.
        if (!wasModified) doc.Modified = false;

        _view = view;
        _viewHwnd = view.Handle;
        _viewportId = view.ActiveViewportID;
        s_liveViews.Add(_viewportId);
        bool again = _hadView;
        _hadView = true;
        if (again) ViewRecreated?.Invoke();
        return _view;
    }

    private void ForgetView()
    {
        if (_view == null) return;
        s_liveViews.Remove(_viewportId);
        _view = null;
        _viewHwnd = IntPtr.Zero;
        _viewportId = Guid.Empty;
    }

    private void CloseView()
    {
        var view = _view;
        bool alive = view != null && IsWindow(_viewHwnd);
        ForgetView();
        if (!alive) return;
        try
        {
            var doc = view!.Document;
            bool wasModified = doc?.Modified ?? true;
            view.Close();
            if (doc != null && !wasModified) doc.Modified = false;
        }
        catch { /* Rhino shutting down: the view goes with the document */ }
    }

    // Draws the hidden view into _frame. A failed capture (Rhino minimized)
    // keeps the last frame.
    private void CaptureFrame()
    {
        if (Capturing) return;
        var size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var view = EnsureView();
        if (view == null) return;
        SizeView(view, size);
        Bitmap? bmp = null;
        Capturing = true;
        try
        {
            // The mode is passed along: without it a hidden view is captured
            // Shaded-looking whatever its display mode is (Rendered showed
            // edges and no materials).
            var mode = view.ActiveViewport.DisplayMode;
            bmp = mode != null ? view.CaptureToBitmap(size, mode) : view.CaptureToBitmap(size);
        }
        catch { /* keep the last frame */ }
        finally { Capturing = false; }
        if (bmp == null) return;
        _frame?.Dispose();
        _frame = bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool hold = HoldPaint?.Invoke() == true;
        if (!CaptureMode)
        {
            if (hold) return;
            try { base.OnPaint(e); }
            finally { RenderTargetReset.Clear(Handle, Viewport); }
            return;
        }
        // Held (GH mid-solve): the last frame is painted again.
        if (!hold) CaptureFrame();
        if (_frame == null) return;
        // Raw GDI copy of the pixels: Graphics.DrawImage took ~7 ms for a
        // 990x793 frame, this takes under 1 ms (measured 2026-11-09).
        var bits = _frame.LockBits(new Rectangle(0, 0, _frame.Width, _frame.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        IntPtr hdc = e.Graphics.GetHdc();
        try
        {
            // Negative height = top-down rows, as LockBits hands them out.
            var info = new BitmapInfoHeader { Size = 40, Width = bits.Width, Height = -bits.Height, Planes = 1, BitCount = 32 };
            SetDIBitsToDevice(hdc, 0, 0, (uint)bits.Width, (uint)bits.Height, 0, 0, 0, (uint)bits.Height, bits.Scan0, ref info, 0);
        }
        finally
        {
            e.Graphics.ReleaseHdc(hdc);
            _frame.UnlockBits(bits);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (CaptureMode || HoldPaint?.Invoke() == true) return;
        base.OnPaintBackground(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!CaptureMode || _view == null || !IsWindow(_viewHwnd)) return;
        var size = ClientSize;
        if (size.Width > 0 && size.Height > 0) SizeView(_view, size);
    }

    // Makes the hidden view's viewport `size` pixels by resizing its floating
    // window (frame included). Not RhinoViewport.Size: that setter shows the
    // window again and makes it Rhino's active view.
    private static void SizeView(RhinoView view, Size size)
    {
        var cur = view.ActiveViewport.Size;
        if (cur == size) return;
        IntPtr root = GetAncestor(view.Handle, GA_ROOT);
        if (!GetWindowRect(root, out var r)) return;
        SetWindowPos(root, IntPtr.Zero, 0, 0,
            size.Width + (r.Right - r.Left) - cur.Width, size.Height + (r.Bottom - r.Top) - cur.Height,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private const uint GA_ROOT = 2;
    private const int SW_HIDE = 0;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern int SetDIBitsToDevice(IntPtr hdc, int x, int y, uint w, uint h,
        int srcX, int srcY, uint startScan, uint scanLines, IntPtr bits, ref BitmapInfoHeader info, uint usage);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    private System.Drawing.Point _prev;
    private System.Drawing.Point _rightDown;
    private bool _rightDragging;
    // Pixels the cursor may wander before a right press counts as a drag.
    private const int ClickSlop = 4;
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
        { var cam = Cam; _pivot = Pivot?.Invoke(cam) ?? cam.CameraTarget; }
        if (e.Button == MouseButtons.Right) { _rightDown = e.Location; _rightDragging = false; }
        PointerDown?.Invoke();
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && !_rightDragging)
        {
            // Navigation starts only past the slop, so a click never nudges the view.
            if (Math.Abs(e.X - _rightDown.X) <= ClickSlop && Math.Abs(e.Y - _rightDown.Y) <= ClickSlop) return;
            _rightDragging = true;
        }
        if (Locked || e.Button != MouseButtons.Right) { _prev = e.Location; return; }

        var vp    = Cam;
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
        var vp = Cam;
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
        ReturnFocus?.Invoke();
        if (e.Button != MouseButtons.Right) return;
        if (!_rightDragging) ContextClick?.Invoke(e.Location);
        else if (!Locked) NavigationEnded?.Invoke();
        _rightDragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Locked || e.Delta == 0) return;
        var at = e.Location;
        var cam = Cam;
        if (!ZoomAtCursor)
        {
            var p = cam.WorldToClient(Pivot?.Invoke(cam) ?? cam.CameraTarget);
            at = new System.Drawing.Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }
        cam.Magnify(e.Delta > 0 ? 1.15 : 1 / 1.15, false, at);
        Refresh();
        _wheelEnd.Stop(); _wheelEnd.Start();   // save the camera once the wheel settles
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _wheelEnd.Dispose(); CloseView(); _frame?.Dispose(); _frame = null; }
        base.Dispose(disposing);
    }
}

// Makes drawing into a non-Rhino window safe.
//
// CRhinoViewport_Draw hands our HWND to the viewport's display pipeline with
// SetRenderTarget(HWND), which stores CWnd::FromHandle(hwnd). For a window
// MFC didn't create that is a temporary object, deleted the next time the
// app goes idle, and the pipeline keeps the pointer: the following draw reads
// it (GetWnd) from freed memory. Clearing the pipeline's render target right
// after each paint — while the object is still alive — leaves nothing stale
// behind; the next draw finds no window and sets the target again.
//
// CleanPreviousRenderTarget is the pipeline's own "forget the target" step
// (it only destroys a window the pipeline owns, which ours never is). It is
// not part of any public API, so both entry points are looked up by name at
// startup; if either is missing, Available is false and nothing is called.
internal static class RenderTargetReset
{
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private delegate IntPtr PipelineOfFn(IntPtr hwnd, IntPtr viewport);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private delegate void CleanFn(IntPtr pipeline);

    private static readonly PipelineOfFn? s_pipelineOf;
    private static readonly CleanFn? s_clean;
    private static readonly System.Reflection.MethodInfo? s_viewportPtr;

    internal static bool Available => s_pipelineOf != null && s_clean != null && s_viewportPtr != null;

    static RenderTargetReset()
    {
        try
        {
            if (IntPtr.Size != 8) return;   // the decorated name below is the x64 one
            IntPtr pipelineOf = GetProcAddress(GetModuleHandle("rhcommon_c.dll"), "CRhinoViewport_DisplayPipeline");
            IntPtr clean = GetProcAddress(GetModuleHandle("RhinoCore.dll"), "?CleanPreviousRenderTarget@CRhinoDisplayPipeline@@AEAAXXZ");
            var viewportPtr = typeof(RhinoViewport).GetMethod("NonConstPointer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null, Type.EmptyTypes, null);
            if (pipelineOf == IntPtr.Zero || clean == IntPtr.Zero || viewportPtr == null) return;
            s_pipelineOf  = (PipelineOfFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(pipelineOf, typeof(PipelineOfFn));
            s_clean       = (CleanFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(clean, typeof(CleanFn));
            s_viewportPtr = viewportPtr;
        }
        catch { /* not available: the caller falls back to capture mode */ }
    }

    // Call right after a paint. The viewport already has its pipeline then,
    // so the lookup returns it and creates nothing.
    internal static void Clear(IntPtr hwnd, RhinoViewport? viewport)
    {
        if (!Available || viewport == null) return;
        try
        {
            var vp = (IntPtr)s_viewportPtr!.Invoke(viewport, null)!;
            if (vp == IntPtr.Zero) return;
            IntPtr pipeline = s_pipelineOf!(hwnd, vp);
            if (pipeline != IntPtr.Zero) s_clean!(pipeline);
        }
        catch { /* never let a paint throw */ }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
