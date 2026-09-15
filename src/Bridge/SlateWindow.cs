using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace Slate.Bridge;

public class SlateWindow : Form
{
    [DllImport("user32.dll")] static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    static readonly IntPtr HWND_TOPMOST    = new(-1);
    static readonly IntPtr HWND_NOTOPMOST  = new(-2);
    const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001;
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

    // ── titlebar icon ────────────────────────────────────────────────────────
    // Form.Icon can't be used for this: under net48 (Rhino 7 compat) its
    // getter always substitutes an embedded "wfc.ico" default the moment you
    // touch the property, even mid-assignment, so per-theme swapping via
    // Icon doesn't work reliably across both runtimes. WM_SETICON sent
    // directly bypasses that and lets each theme show its own brand mark
    // (SlateLogo): coloured for light, monochrome for dark — see
    // ApplyTitleBarTheme, which already runs on every theme change.
    [DllImport("user32.dll")] static extern IntPtr SendMessage(
        IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    const int WM_SETICON  = 0x0080;
    const int ICON_SMALL  = 0;
    const int ICON_BIG    = 1;

    // ── Alt key state (corner-drag spanning hint) ───────────────────────────────
    // Windows treats Alt as a "system key" (WM_SYSKEYUP) — its release can get
    // swallowed before ever reaching WebView2's own DOM, especially mid corner-
    // drag, leaving the JS side's keydown/keyup tracking stuck thinking Alt is
    // still held (see CornerHandle.svelte / App.svelte's altHeld store, which
    // already has a pointermove-based self-heal for this — that helps but can't
    // fully cover it, since it only fires on Chromium's own event, delivered
    // through the same shaky path). GetAsyncKeyState reads the OS's live
    // physical key state directly, independent of focus routing or whatever
    // Chromium's internal Alt/menu-mnemonic handling decides to forward, so
    // polling it here and pushing the result to JS sidesteps the problem
    // entirely instead of chasing it symptom by symptom.
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    const int VK_MENU = 0x12;

    private readonly System.Windows.Forms.Timer _altPollTimer = new() { Interval = 30 };
    private bool _altHeld;

    // ── 'c'/'x'/'g' hotkey state (capture / delete / group) ──────────────────────
    // Same underlying problem as Alt above, different trigger: Rhino's own
    // command-line textbox can silently steal keyboard focus from the WebView2
    // over a long session (a general Rhino/Eto focus-routing issue, not
    // specific to this plugin — typing into a GH canvas rename box has the same
    // symptom), so the DOM's own keydown for 'c'/'x'/'g' can go quiet with no
    // visible sign anything's wrong — worse for 'g' specifically, since the GH
    // canvas has its own native "Group" command bound to the same key, so a
    // stolen keydown doesn't just silently no-op, it groups objects on the GH
    // canvas instead. Polled here the same way: GetAsyncKeyState reads live
    // physical key state independent of focus, gated on the mouse actually
    // being over Slate's window (mirrors the hover-based semantics the
    // hotkeys already have on the JS side — this also stops a 'g' typed while
    // deliberately using GH's own canvas shortcut from leaking into Slate) and
    // edge-detected so a held key doesn't repeat-fire every 30ms.
    const int VK_C = 0x43, VK_X = 0x58, VK_G = 0x47;
    private bool _cHeld, _xHeld, _gHeld;

    static Icon? _iconLight, _iconDark;

    static void ApplyWindowIcon(IntPtr hWnd, bool dark)
    {
        // Built once and cached — see SlateLogo.ToIcon's own note on the
        // one-time GDI handle cost.
        //
        // The dark variant used to be the SAME colour (0xefefef) for both
        // outline and fill — with zero contrast between the two overlapping
        // squares, the brand mark collapses into a single featureless white
        // blob at 32x16 titlebar-icon scale (confirmed by rendering it in
        // isolation — this is a drawing/colour-contrast issue, not a
        // WM_SETICON/GetHicon() delivery bug like the earlier fixes here
        // assumed). favicon.svg's own dark-theme variant already solved this
        // the right way (light outline + accent-blue fill, see app.css
        // --accent: #74a2ff) — mirrored here instead of true monochrome.
        _iconLight ??= SlateLogo.ToIcon(32, outline: Color.FromArgb(0x20, 0x1e, 0x1d), fill: Color.FromArgb(0x5b, 0x8e, 0xf5));
        _iconDark  ??= SlateLogo.ToIcon(32, outline: Color.FromArgb(0xe8, 0xeb, 0xf0), fill: Color.FromArgb(0x74, 0xa2, 0xff));

        var icon = (dark ? _iconDark : _iconLight).Handle;
        SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, icon);
        SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG,   icon);

        // WM_SETICON alone doesn't repaint the caption when sent before the
        // window is first shown (e.g. from OnHandleCreated) — DWM hasn't
        // composited the titlebar yet, so the icon silently doesn't appear
        // until something else forces a non-client repaint (which is why it
        // only showed up after a theme toggle before this fix). Force that
        // repaint immediately instead of waiting for one.
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    // ── dark title bar (Windows 11) ─────────────────────────────────────────
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_BORDER_COLOR            = 34;
    const int DWMWA_CAPTION_COLOR           = 35;
    const int DWMWA_TEXT_COLOR              = 36;

    // COLORREF is 0x00BBGGRR, not RGB — build it from the palette's own tones
    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // Colours mirror app.css's --bg/--text for each theme, dark or light.
    void ApplyTitleBarTheme(bool dark)
    {
        var captionColor = dark ? Color.FromArgb(26, 26, 26)   : Color.FromArgb(228, 225, 216);
        var caption = ColorRef(captionColor);
        var text    = ColorRef(dark ? Color.FromArgb(239, 239, 239) : Color.FromArgb(42, 39, 36));
        var enabled = dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        ApplyWindowIcon(Handle, dark);

        // Form.BackColor was hardcoded near-black regardless of theme — it's
        // what shows through for a frame before WebView2 has painted (or in
        // any gap around it), so a light-mode session could catch a dark
        // flash there. Mirror it to the same tone as the caption instead.
        BackColor = captionColor;

        // WebView2's OWN default background (shown by Chromium's compositor
        // for any not-yet-painted frame — distinct from Form.BackColor above,
        // which only covers gaps outside the control) was never set, so it
        // defaulted to white. Most visible during a live left/top-edge window
        // drag: unlike right/bottom, that simultaneously moves AND resizes the
        // window, which gives the compositor more to redo per tick and more
        // chances to show an unpainted frame — this doesn't stop that, but
        // makes the flash match the theme instead of flashing white.
        _webView.DefaultBackgroundColor = captionColor;
    }

    string _lastAppliedTheme = "dark";

    // Hardcoding "dark" here and waiting for the async WebView2 → JS → JS-sends-
    // state_snapshot round trip to correct it (via OnMessageFromJs) meant the
    // titlebar/icon only ever matched a persisted "light" theme once that whole
    // chain completed — invisible in practice on Rhino 8, but on Rhino 7 (no
    // bundled WebView2 runtime, see Slate.csproj) that chain can stall or never
    // finish, leaving the bar black and the brand-mark icon missing indefinitely.
    // Reading the theme straight out of already-available state (reopened-in-
    // session snapshot, or the file's persisted ui_state) makes the correct
    // theme apply synchronously, with no dependency on WebView2 ever loading.
    static string PeekPendingTheme()
    {
        string? json = (HostDocument != null && _uiStateByDoc.TryGetValue(HostDocument, out var s)) ? s : PendingFileState;
        if (json == null) return "dark";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("theme", out var th))
                return th.GetString() ?? "dark";
        }
        catch { /* malformed/legacy state — fall back to dark */ }
        return "dark";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _lastAppliedTheme = PeekPendingTheme();
        ApplyTitleBarTheme(_lastAppliedTheme != "light");
    }

    private readonly WebView2 _webView = new();
    private readonly Dictionary<string, GH_NumberSlider> _sliders = new();
    private readonly Dictionary<string, GH_BooleanToggle> _toggles = new();
    private readonly Dictionary<string, GH_ButtonObject> _buttons = new();
    private readonly Dictionary<string, GH_ValueList> _valueLists = new();
    private readonly Dictionary<string, GH_Panel> _panels = new();
    private readonly Dictionary<string, GH_ItemPicker> _itemPickers = new();

    // "Item Selector" from the Human plugin — Human.GH_ValueList, structurally
    // a clone of core GH_ValueList (ListItems/SelectItem) but fed by a wired
    // input. Reflected rather than referenced at compile time, so Slate still
    // builds and runs fine on a machine without Human installed.
    private readonly Dictionary<string, IGH_Param> _humanValueLists = new();
    private readonly Dictionary<string, GH_ColourSwatch> _colourPickers = new();

    // Pancake plugin's "True Only Button" (Pancake.GH.Params.TrueOnlyBtn) — a
    // GH_Param subclass with the same ButtonDown shape as core GH_ButtonObject,
    // reflected rather than referenced at compile time so Slate still builds
    // and runs fine on a machine without Pancake installed.
    private readonly Dictionary<string, IGH_Param> _pancakeTrueOnlyButtons = new();

    // Native "Trigger" component (Grasshopper.Kernel.Special.GH_Timer) — a
    // plain document object, not an IGH_Param like every other captured
    // control above (no data output; it only fires ExpireTargets() on its
    // wired targets).
    private readonly Dictionary<string, GH_Timer> _triggers = new();

    // Live GH canvas position for an already-captured id — checks every
    // per-type registry above (same set RestoreState re-attaches from). Used
    // only for the ephemeral "sort_positions_request" round trip; never
    // cached or persisted.
    private bool TryGetLivePivot(string id, out System.Drawing.PointF pivot)
    {
        if (_sliders.TryGetValue(id, out var sl))          { pivot = sl.Attributes.Pivot;  return true; }
        if (_toggles.TryGetValue(id, out var tg))           { pivot = tg.Attributes.Pivot;  return true; }
        if (_buttons.TryGetValue(id, out var bt))           { pivot = bt.Attributes.Pivot;  return true; }
        if (_valueLists.TryGetValue(id, out var vl))        { pivot = vl.Attributes.Pivot;  return true; }
        if (_panels.TryGetValue(id, out var pn))            { pivot = pn.Attributes.Pivot;  return true; }
        if (_itemPickers.TryGetValue(id, out var ip))       { pivot = ip.Attributes.Pivot;  return true; }
        if (_humanValueLists.TryGetValue(id, out var hv))   { pivot = hv.Attributes.Pivot;  return true; }
        if (_colourPickers.TryGetValue(id, out var cp))     { pivot = cp.Attributes.Pivot;  return true; }
        if (_pancakeTrueOnlyButtons.TryGetValue(id, out var pb)) { pivot = pb.Attributes.Pivot; return true; }
        if (_triggers.TryGetValue(id, out var tr))          { pivot = tr.Attributes.Pivot;  return true; }
        pivot = default;
        return false;
    }

    // 8-digit hex (#RRGGBBAA) so alpha round-trips through the wire alongside RGB.
    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
    private static Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        int a = hex.Length >= 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) : 255;
        return Color.FromArgb(a, r, g, b);
    }
    private static Type? _humanValueListType;
    private static bool  _humanReflectionChecked;

    public static bool IsHumanValueList(object obj)
    {
        EnsureHumanReflection();
        return _humanValueListType != null && _humanValueListType.IsInstanceOfType(obj);
    }

    private static void EnsureHumanReflection()
    {
        if (_humanReflectionChecked) return;
        _humanReflectionChecked = true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Human");
            _humanValueListType = asm?.GetType("Human.GH_ValueList");
        }
        catch { _humanValueListType = null; }
    }

    private static string GetHumanListMode(object obj) =>
        _humanValueListType?.GetProperty("ListMode")?.GetValue(obj)?.ToString() ?? "DropDown";

    private static (List<string> options, object value, bool multi, bool cycle, bool loop) GetHumanListItems(object obj)
    {
        var options  = new List<string>();
        var itemList = new List<object>();
        var items = _humanValueListType?.GetProperty("ListItems")?.GetValue(obj) as System.Collections.IEnumerable;

        PropertyInfo? nameProp = null, selProp = null;
        if (items != null)
        {
            foreach (var item in items)
            {
                nameProp ??= item.GetType().GetProperty("Name");
                selProp  ??= item.GetType().GetProperty("Selected");
                options.Add(nameProp?.GetValue(item) as string ?? "");
                itemList.Add(item);
            }
        }

        var mode  = GetHumanListMode(obj);
        // Same reclassification as the native GH_ValueList path — see
        // IsMultiSelectMode/IsCycleMode's comment.
        bool multi = mode == "CheckList";
        object value;
        if (multi)
        {
            // Same reasoning as the native GH_ValueList path: SelectedItems (not a
            // ListItems scan) is what preserves Sequence's click order.
            var selectedItems = _humanValueListType?.GetProperty("SelectedItems")?.GetValue(obj) as System.Collections.IEnumerable;
            var indices = new List<int>();
            if (selectedItems != null)
                foreach (var si in selectedItems) { int idx = itemList.IndexOf(si); if (idx >= 0) indices.Add(idx); }
            value = indices;
        }
        else
        {
            int selIdx = -1;
            for (int i = 0; i < itemList.Count; i++)
                if (selProp?.GetValue(itemList[i]) is true) { selIdx = i; break; }
            value = selIdx;
        }
        // Cycle wraps at the ends; Sequence stops there (matches GH's own
        // arrow widget — Sequence's arrows disable/no-op past the last item).
        return (options, value, multi, mode == "Cycle" || mode == "Sequence", mode == "Cycle");
    }

    private static void SelectOrToggleHumanItem(object obj, int index)
    {
        var mode = GetHumanListMode(obj);
        var methodName = mode == "CheckList" ? "ToggleItem" : "SelectItem";
        _humanValueListType?.GetMethod(methodName, new[] { typeof(int) })?.Invoke(obj, new object[] { index });
    }

    private static Type? _pancakeTrueOnlyBtnType;
    private static bool  _pancakeReflectionChecked;

    public static bool IsPancakeTrueOnlyButton(object obj)
    {
        EnsurePancakeReflection();
        return _pancakeTrueOnlyBtnType != null && _pancakeTrueOnlyBtnType.IsInstanceOfType(obj);
    }

    private static void EnsurePancakeReflection()
    {
        if (_pancakeReflectionChecked) return;
        _pancakeReflectionChecked = true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Pancake");
            _pancakeTrueOnlyBtnType = asm?.GetType("Pancake.GH.Params.TrueOnlyBtn");
        }
        catch { _pancakeTrueOnlyBtnType = null; }
    }

    private static bool GetPancakeButtonDown(object obj) =>
        _pancakeTrueOnlyBtnType?.GetProperty("ButtonDown")?.GetValue(obj) is true;

    private static void SetPancakeButtonDown(object obj, bool value) =>
        _pancakeTrueOnlyBtnType?.GetProperty("ButtonDown")?.SetValue(obj, value);

    private static SlateWindow? _instance;

    // ── logging ──────────────────────────────────────────────────────────────
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> LogQueue = new();
    public static Grasshopper.Kernel.GH_Component? HostComponent { get; set; }

    // Off by default — trace-level detail (every state_snapshot accepted,
    // every SyncToDocument sync/skip, every Write() dedup-skip) is only
    // useful while actively chasing a state-sync bug, not on every normal
    // solve. Toggled
    // from SlatePanel's right-click menu (not persisted — resets to off each
    // Rhino session, so it can't accidentally get baked into a saved .gh and
    // spam a client's log). See DebugLog below.
    public static bool DebugLogging { get; set; }

    private static void DebugLog(string msg)
    {
        if (DebugLogging) Log($"[debug] {msg}");
    }

    // Thin public wrapper so SlatePanel.Write()/Read() (same geometry
    // persistence path as ApplyGeometry's own DebugLog call) can log under
    // the same "Debug logging" toggle without exposing the whole Log/
    // DebugLog machinery.
    public static void DebugLogGeometry(string msg) => DebugLog(msg);

    private static Grasshopper.Kernel.GH_Document? _hostDocument;
    public static Grasshopper.Kernel.GH_Document? HostDocument
    {
        get => _hostDocument;
        set
        {
            if (_hostDocument == value) return;
            if (_hostDocument != null)
            {
                _hostDocument.SolutionEnd -= OnDocSolutionEnd;
                _hostDocument.ModifiedChanged -= OnDocModifiedChanged;
            }
            _hostDocument = value;
            _latestValues.Clear();
            _latestColourValues.Clear();
            _solveRunning = false;

            // The live GH object references (_sliders etc.) belong to whichever
            // document last owned them — reusing them for a different document
            // (multiple .gh files open as tabs, each with its own Slate panel)
            // would apply slider drags to the wrong file's objects. Always drop
            // them on an actual document change; the per-document ui_state cache
            // below (keyed by document, not a single slot) is what lets the
            // right tab/group layout come back when switching back to a document.
            _instance?.ClearDicts();

            if (_hostDocument != null)
            {
                _hostDocument.SolutionEnd += OnDocSolutionEnd;
                _hostDocument.ModifiedChanged += OnDocModifiedChanged;
            }
        }
    }

    // Keyed by document (not a single slot) so multiple open .gh files, each
    // with their own Slate panel, keep their own tabs/groups/values in memory
    // independently — switching the active GH tab (see the DocumentChanged
    // hook wired up in SlateAssemblyPriority) looks up this document's own
    // entry instead of showing whatever the previously-active file left behind.
    // Updated on every "state_snapshot" push from JS (see OnMessageFromJs) and
    // seeded from a file's persisted ui_state when it's read (see SlatePanel.Read
    // → SeedDocumentState). Entries are intentionally never evicted on document
    // close — GH_Document instances are cheap to keep and this avoids losing a
    // background tab's state if it gets closed and reopened via Undo.
    private static readonly Dictionary<Grasshopper.Kernel.GH_Document, string> _uiStateByDoc = new();

    public static string? GetSerializedState(Grasshopper.Kernel.GH_Document? doc) =>
        doc != null && _uiStateByDoc.TryGetValue(doc, out var s) ? s : null;

    // Populated in OnDocModifiedChanged (the dirty→clean transition, i.e. a
    // real disk save) — separate from StampSavedAt's per-payload timestamp so
    // DescribeCurrentState can report "last saved" even when GetSerializedState
    // is empty (nothing captured yet) or hasn't changed since the last save.
    private static readonly Dictionary<Grasshopper.Kernel.GH_Document, string> _lastSavedAt = new();

    // Backs the SlatePanel's "State" output — a live, always-current snapshot
    // of what's captured for THIS document (file name, item counts, last real
    // save time), as opposed to "Log", which only shows recent events and
    // goes back to empty after GH drains it. Meant to answer "is anything
    // actually going to be saved right now" at a glance, without needing to
    // scroll back through the log or trigger a fresh capture/save to find out.
    public static string DescribeCurrentState(Grasshopper.Kernel.GH_Document? doc)
    {
        if (doc == null) return "No document.";
        var state = GetSerializedState(doc);
        var counts = state == null ? "no captured UI" : SummarizeStateCounts(state);
        var savedAt = _lastSavedAt.TryGetValue(doc, out var s) ? s : "never saved this session";
        return $"{doc.DisplayName}: {counts} (last saved: {savedAt})";
    }

    // Write() runs on every GH_IO serialization of this document — not just an
    // actual Ctrl+S, but every undo-checkpoint and copy/paste too — so logging
    // it unconditionally would spam the output on completely unrelated canvas
    // edits elsewhere in the file. Keyed by doc and compared against the last
    // logged payload so it only ever logs when the CONTENT about to be written
    // actually changed (a real capture, layout edit, or drag that GH is now
    // persisting), which in practice tracks "what will end up on disk next
    // save" closely enough to answer "kaç slider kayıtlı" without the noise.
    private static readonly Dictionary<Grasshopper.Kernel.GH_Document, string> _lastLoggedWriteState = new();

    // Stamped into the JSON that actually gets written to disk (not into the
    // live _uiStateByDoc cache — that keeps getting overwritten by the next
    // state_snapshot regardless) so that on the NEXT open, RestoreState can
    // report exactly when the layout it's restoring was captured, answering
    // "dosya ne zaman kaydedildi" without GH exposing a per-object save time.
    //
    // Cached per doc against the UNSTAMPED input: Write() runs on every
    // undo-checkpoint, not just a real Ctrl+S (see LogWriteSummary above), so
    // minting a fresh DateTime.Now every call made the written bytes differ
    // on every single checkpoint even when nothing was actually captured —
    // which is exactly what GH's undo/IO layer uses to decide the document
    // changed, so the file stayed permanently "modified" right after saving.
    // Returning the previous byte-identical stamped output when the raw
    // state hasn't changed keeps consecutive checkpoints truly identical;
    // only a genuine capture/edit (raw state differs) earns a new timestamp.
    private static readonly Dictionary<Grasshopper.Kernel.GH_Document, (string raw, string stamped)> _lastStamped = new();

    public static string? StampSavedAt(Grasshopper.Kernel.GH_Document? doc, string? state)
    {
        if (state == null) return null;
        if (doc != null && _lastStamped.TryGetValue(doc, out var cached) && cached.raw == state)
            return cached.stamped;
        try
        {
            var node = JsonNode.Parse(state)!.AsObject();
            node["savedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var stamped = node.ToJsonString();
            if (doc != null) _lastStamped[doc] = (state, stamped);
            return stamped;
        }
        catch { return state; }
    }

    public static void LogWriteSummary(Grasshopper.Kernel.GH_Document? doc, string? state)
    {
        if (doc == null) return;
        var key = state ?? "\0empty";
        if (_lastLoggedWriteState.TryGetValue(doc, out var last) && last == key)
        {
            DebugLog($"Write: skipped logging — content unchanged since last Write for doc={doc.DisplayName}.");
            return;
        }
        _lastLoggedWriteState[doc] = key;

        Log(state == null
            ? "Write: no captured UI for this document yet — nothing to save."
            : $"Write: saving {SummarizeStateCounts(state)}.");
    }

    // Same tree shape RestoreState walks (workspaces → layout tree → tabs →
    // sliders/groups), but read-only — just tallies item "type" fields
    // instead of re-binding them to live GH objects.
    private static string SummarizeStateCounts(string stateJson)
    {
        try
        {
            var state = JsonNode.Parse(stateJson)!.AsObject();
            var counts = new Dictionary<string, int>();
            void Bump(string kind) { counts.TryGetValue(kind, out var c); counts[kind] = c + 1; }

            void CountItems(JsonArray? arr)
            {
                if (arr == null) return;
                foreach (var item in arr)
                    Bump(item!.AsObject()["type"]?.GetValue<string>() ?? "slider");
            }
            void CountTab(JsonObject tab)
            {
                CountItems(tab["sliders"]?.AsArray());
                if (tab["groups"] is JsonArray groups)
                    foreach (var g in groups)
                        CountItems(g!.AsObject()["sliders"]?.AsArray());
            }
            void CountNode(JsonObject node)
            {
                var type = node["type"]?.GetValue<string>();
                if (type == "leaf")
                {
                    if (node["tabs"] is JsonArray tabs)
                        foreach (var t in tabs) CountTab(t!.AsObject());
                }
                else if (type == "split")
                {
                    if (node["a"] is JsonObject a) CountNode(a);
                    if (node["b"] is JsonObject b) CountNode(b);
                }
                else if (node["tabs"] is JsonArray legacyTabs)
                    foreach (var t in legacyTabs) CountTab(t!.AsObject());
            }

            if (state["workspaces"] is JsonArray workspacesArr)
                foreach (var w in workspacesArr)
                    if (w!.AsObject()["layout"] is JsonObject wLayout) CountNode(wLayout);
            else if (state["layout"] is JsonObject layoutNode)
                CountNode(layoutNode);
            else
                CountNode(state);

            return counts.Count == 0
                ? "0 item(s)"
                : string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value} {kv.Key}(s)"));
        }
        catch (Exception ex) { return $"<count error: {ex.GetType().Name}: {ex.Message}>"; }
    }

    // Only used to seed the very first restore for a freshly-Read document —
    // see SeedDocumentState and the "ui_ready" case in OnMessageFromJs.
    public static string? PendingFileState      { get; set; }
    public static bool     NeedsFileRestore      { get; set; }

    // Called from SlatePanel.Read() so a document's saved ui_state is known
    // here as soon as the file loads, even before it ever becomes the active
    // tab (DocumentChanged can then restore it immediately on first switch,
    // instead of showing a blank panel until something else pushes state).
    public static void SeedDocumentState(Grasshopper.Kernel.GH_Document doc, string? state)
    {
        if (state != null && !_uiStateByDoc.ContainsKey(doc))
            _uiStateByDoc[doc] = state;
    }

    // ── window geometry persistence ─────────────────────────────────────────
    public static Size?  PendingWindowSize     { get; set; }
    public static Point? PendingWindowLocation { get; set; }

    // Static, not GH_IO-serialized: survives a Show/Hide toggle recreating
    // _instance (GetOrCreate() builds a fresh SlateWindow — and thus a fresh
    // DefaultWindowSize — whenever _instance is null or disposed) within the
    // same Rhino session, distinct from PendingWindowSize which only carries
    // the size across an actual file Read().
    private static Size?  _lastKnownSize;
    private static Point? _lastKnownLocation;

    // Keyed by document, same reasoning as _uiStateByDoc above: each open .gh
    // file can have its own window size/position, and switching the active GH
    // tab (OnActiveDocumentChanged) needs to re-apply THAT file's geometry
    // instead of leaving whatever the previously-active file's window looked
    // like. Kept updated live by the Resize/Move handlers in the constructor
    // (stamped against whichever document is HostDocument at the time) and
    // seeded from a file's persisted win_w/h/x/y on Read (SeedDocumentGeometry).
    private static readonly Dictionary<Grasshopper.Kernel.GH_Document, Size>  _sizeByDoc     = new();
    private static readonly Dictionary<Grasshopper.Kernel.GH_Document, Point> _locationByDoc = new();

    // While WindowState is Maximized (or Minimized), Form.Size/Location report
    // OS-level maximized bounds — on Windows 10/11 these include an invisible
    // resize border, so a screen filled edge-to-edge reads back as a few px
    // OVER the monitor's actual size with a small NEGATIVE Location (observed
    // live: 2576x1456 at (-8,-8) on a 2560x1440 screen). Saving that raw is
    // what left the window opening oversized and pinned near the top-left on
    // the next file open.
    //
    // Deliberately NOT using RestoreBounds (WinForms' tracked pre-maximize
    // bounds) here either — an earlier version did, plus a separate
    // win_maximized flag applied via FormWindowState.Maximized on restore.
    // Dropped 2026-09-15: the user doesn't want maximize tracked as a
    // distinct state at all ("tam ekran mı değil mi pek umrumda değil, tam
    // ekransa loc/genişlik/yükseklik değeri bellidir zaten") — if the window
    // was filling the screen when saved, what should come back is a plain
    // window sized to that screen's actual usable pixels, nothing more. So
    // when maximized, this reads the CURRENT screen's WorkingArea (excludes
    // the taskbar, matching what a real maximize visually fills) and hands
    // that back as if it were an ordinary Normal size/location — restoring
    // it later is then just the same plain Size/Location assignment as any
    // other saved geometry, never touching WindowState. That also sidesteps
    // the singleton-hidden-window unreliability explored in
    // pencere-boyutu-maximize.md for actually applying FormWindowState.
    public static Size?  GetCurrentWindowSize()     => _instance != null && !_instance.IsDisposed
        ? (_instance.WindowState == FormWindowState.Normal ? _instance.Size     : Screen.FromControl(_instance).WorkingArea.Size)
        : (Size?)null;
    public static Point? GetCurrentWindowLocation()  => _instance != null && !_instance.IsDisposed
        ? (_instance.WindowState == FormWindowState.Normal ? _instance.Location : Screen.FromControl(_instance).WorkingArea.Location)
        : (Point?)null;

    // Used by SlatePanel.Write() so a document's saved geometry reflects ITS
    // OWN last-known size/position, not whatever the window currently looks
    // like if a different tab is active when that document happens to be
    // saved. Falls back to the live window geometry when this document has no
    // cached entry yet (e.g. its first-ever save this session while active).
    //
    // For the document the window is CURRENTLY showing (doc == HostDocument),
    // always reads the live window instead of the cache: the cache is only
    // refreshed by the Resize/Move handlers below while WindowState is
    // Normal, so it goes stale the moment the active window is maximized —
    // reading it here would silently save the size/position from before the
    // user maximized, not the screen-filling px GetCurrentWindowSize/
    // Location compute for that case.
    public static Size?  GetWindowSize(Grasshopper.Kernel.GH_Document? doc) =>
        doc != null && doc != HostDocument && _sizeByDoc.TryGetValue(doc, out var s) ? s : GetCurrentWindowSize();
    public static Point? GetWindowLocation(Grasshopper.Kernel.GH_Document? doc) =>
        doc != null && doc != HostDocument && _locationByDoc.TryGetValue(doc, out var p) ? p : GetCurrentWindowLocation();

    public static void SeedDocumentGeometry(Grasshopper.Kernel.GH_Document doc, Size? size, Point? location)
    {
        if (size     is Size  sz && !_sizeByDoc.ContainsKey(doc))     _sizeByDoc[doc]     = sz;
        if (location is Point pt && !_locationByDoc.ContainsKey(doc)) _locationByDoc[doc] = pt;
    }

    const int MinWinW = 320, MinWinH = 480, MaxWinW = 3000, MaxWinH = 2000;

    // Size actually requested (e.g. from a file's saved win_w/win_h) before
    // the screen-fit clamp below potentially shrinks it — RestoreState
    // compares this against the window's live (possibly now-smaller) size to
    // know whether the saved layout's pane sizeA proportions need rescaling
    // for a different screen (see coklu-ekran-cozunurluk.md: a layout
    // authored on a 5K monitor opened on a 4K one used to leave its last
    // ~20% hidden off-screen with no way to recover it short of manually
    // resizing every pane).
    private static Size? _lastRequestedWindowSize;

    // Single choke point for applying window geometry — used both for a
    // freshly opened file's saved win_w/h/x/y and for switching back to an
    // already-open document's remembered size/position. Picks whichever
    // screen the target location actually lands on (falling back to the
    // nearest one if it's off every connected monitor, e.g. a second display
    // from a previous session that's no longer plugged in) and clamps both
    // size and position to THAT screen's working area — not just the fixed
    // Min/MaxWin* bounds, which say nothing about what the current screen
    // can actually show.
    private static void ApplyGeometry(SlateWindow w, Size size, Point loc)
    {
        // The window is a singleton reused across every document (see
        // GetOrCreate/_sizeByDoc above) — WindowState is never part of what's
        // persisted (see GetCurrentWindowSize/Location above for why: a
        // maximized save is flattened into plain screen-filling px, never a
        // distinct state), but it's also never reset here on its own, so a
        // window left Maximized from a previous document/file stays
        // VISUALLY maximized no matter what Size/Location this call goes on
        // to set: while Maximized, those setters only update RestoreBounds
        // (what the window snaps back to once un-maximized), not what's on
        // screen. Confirmed live 2026-09-14: a file saved perfectly normal
        // still opened full-screen because the singleton was left maximized
        // by whichever file was tested right before it. So Size/Location are
        // always computed and applied in Normal state first (forcing it if
        // needed) — the block below may switch back to Maximized afterward,
        // but only once these are the real Bounds it's switching FROM, not
        // whatever stale RestoreBounds Maximized would otherwise keep.
        if (w.WindowState != FormWindowState.Normal) w.WindowState = FormWindowState.Normal;

        _lastRequestedWindowSize = size;

        var screen = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains(loc.X + 20, loc.Y + 20))
                     ?? Screen.FromPoint(loc);
        var wa = screen.WorkingArea;

        int width  = Math.Max(MinWinW, Math.Min(Math.Min(MaxWinW, size.Width),  wa.Width));
        int height = Math.Max(MinWinH, Math.Min(Math.Min(MaxWinH, size.Height), wa.Height));
        w.Size = new Size(width, height);

        int x = Math.Max(wa.Left, Math.Min(loc.X, wa.Right  - width));
        int y = Math.Max(wa.Top,  Math.Min(loc.Y, wa.Bottom - height));
        w.Location = new Point(x, y);

        // A request that exactly fills this screen's working area is what
        // GetCurrentWindowSize/Location produce when the window really WAS
        // Maximized at save time (flattened into plain px above, not a
        // separate persisted state — see those methods). But leaving the
        // window merely Normal-sized to match doesn't look the same:
        // Windows only compensates for a top-level window's invisible resize
        // border (the same border behind the maximized-bounds inflation
        // noted above) when the window is ACTUALLY Maximized. A Normal
        // window sized to the working area keeps that border baked inside
        // its Bounds instead, so its visible edge sits a few px short of the
        // screen edge on the sides/bottom — reads as "almost but not quite"
        // fullscreen. So when the numbers land exactly on a full working-
        // area fill, hand it to a real Maximized state and let Windows do
        // its own border math, instead of trying to replicate it by hand.
        if (width == wa.Width && height == wa.Height && x == wa.Left && y == wa.Top)
            w.WindowState = FormWindowState.Maximized;

        DebugLog($"ApplyGeometry: requested size={size} loc={loc} -> screen={screen.DeviceName} wa={wa} -> applied size={w.Size} loc={w.Location} state={w.WindowState}");
    }

    // Called when OnActiveDocumentChanged switches HostDocument to a document
    // that's never had Resize/Move fire against it this session (e.g. its
    // ui_state was seeded from file but it hasn't been the active tab yet).
    private static void ApplyGeometryForDocument(Grasshopper.Kernel.GH_Document doc)
    {
        // The window is a singleton that outlives document switches (Hide
        // just hides it, GetOrCreate() reuses the same _instance) — so with
        // no per-doc entry, doing nothing here left it at whatever size/
        // location the PREVIOUSLY active document happened to leave it at,
        // same bug class as ResetAll's "no cached state → blank" but missed
        // for geometry. Fall back to the same defaults the constructor uses
        // for a session's very first window.
        var w = GetOrCreate();
        var size     = _sizeByDoc.TryGetValue(doc, out var sz) ? sz : DefaultWindowSize;
        var location = _locationByDoc.TryGetValue(doc, out var loc) ? loc : DefaultWindowLocation;
        ApplyGeometry(w, size, location);
    }

    // Every write to HostDocument must go through here (the null-out in
    // EvictDocument is the one safe exception — the window is hidden by then).
    // Skipping this is what let a freshly-placed SlatePanel show whichever
    // document's UI happened to still be on screen: the HostDocument setter
    // only drops the C# object-reference dicts, it never touches the JS/DOM
    // side, so without an explicit RestoreState/ClearAll here the WebView
    // keeps rendering the PREVIOUS document's tabs. See cozulen-problemler.md,
    // "Yeni Slate component eklenince ... " (2026-08-31).
    //
    // `deferIfRestoring`: only `AddedToDocument` (opening/placing on a file
    // mid-deserialization) needs the BeginInvoke deferral below, and only
    // when there's cached state to restore — GH adds objects in file order,
    // so if this SlatePanel comes before some of its own captured
    // sliders/panels, doc.Objects doesn't have them yet at this exact point
    // and RestoreState would silently drop them as "not found" (a loss that
    // then gets baked into the NEXT save). Confirmed live 2026-09-01: a fully
    // synchronous restore here lost captured items on reload for exactly this
    // reason (see multi-window-state-sync.md).
    //
    // ResetAll has no such object-lookup dependency (there's nothing to find
    // in an empty/new document), and OnActiveDocumentChanged's tab-switch
    // path never runs mid-file-load — both call this with the default
    // `false` and sync immediately. This closes the race a blanket deferral
    // used to leave open: a capture landing in the still-showing PREVIOUS
    // document's DOM right after AddedToDocument, only to be silently wiped
    // when the deferred ResetAll/RestoreState finally ran afterward and
    // cleared that DOM out from under it — the actual cause of "capture
    // looked fine but Save/State showed nothing" on a freshly-placed panel.
    // (An epoch counter used to guard the JS→C# side of this same gap by
    // rejecting snapshots tagged with a stale sync generation — removed
    // 2026-09-07: it never had confirmed evidence of fixing the original
    // cross-document poisoning it targeted, and it was provably discarding
    // legitimate captures made in this exact window instead. Closing the gap
    // itself, rather than filtering messages that fall into it, needs no such
    // guard.)
    public static void SyncToDocument(Grasshopper.Kernel.GH_Document doc, bool deferIfRestoring = false)
    {
        if (doc == HostDocument) return;
        HostDocument = doc;
        ApplyGeometryForDocument(doc);

        bool hasCachedState = _uiStateByDoc.ContainsKey(doc);

        void DoSync()
        {
            // Superseded by a later switch before this ran — leave the
            // window to whatever that one set up instead of clobbering it.
            if (HostDocument != doc)
            {
                DebugLog($"SyncToDocument: deferred sync for doc={doc.DisplayName} skipped — superseded by a later switch to {HostDocument?.DisplayName ?? "null"}.");
                return;
            }
            if (_uiStateByDoc.TryGetValue(doc, out var json))
            {
                DebugLog($"SyncToDocument: RestoreState running for doc={doc.DisplayName}.");
                GetOrCreate().RestoreState(json, doc);
            }
            else
            {
                DebugLog($"SyncToDocument: ResetAll running for doc={doc.DisplayName} — no cached ui_state.");
                GetOrCreate().ResetAll();
            }
        }

        if (deferIfRestoring && hasCachedState)
        {
            var win = GetOrCreate();
            _ = win.Handle; // force handle creation so BeginInvoke below always works, even on this session's very first sync
            win.BeginInvoke((Action)DoSync);
        }
        else
        {
            DoSync();
        }
    }

    // ── solve throttle (latest-value batching) ───────────────────────────────
    // Only the most recent value per slider ID survives to the next solve.
    // SetSliderValue is called exactly once per slider per solve, never on every IPC message.
    private static readonly Dictionary<string, double> _latestValues = new();
    // Colour is a hex string, not a double — same batching, its own dictionary,
    // shares the _solveRunning gate so a drag on either kind of control coalesces
    // into the same single-solve cycle instead of flooding ExpireSolution calls.
    private static readonly Dictionary<string, string> _latestColourValues = new();
    private static bool _solveRunning;

    private static void OnDocSolutionEnd(object sender, Grasshopper.Kernel.GH_SolutionEventArgs e)
    {
        // SolutionEnd may fire on GH background thread — marshal to UI thread for thread safety.
        var win = _instance;
        win?.BeginInvoke((Action)(() =>
        {
            if (_latestValues.Count == 0 && _latestColourValues.Count == 0) _solveRunning = false;
            else ApplyLatestValues();
            PushSliderNameUpdates();
            PushPanelTextUpdates();
            PushItemPickerUpdates();
            PushHumanValueListUpdates();
            PushColourPickerUpdates();
            PushTriggerUpdates();
        }));
    }

    // GH_Document.Modified flips false exactly when Save/SaveQuiet/SaveAs
    // finishes writing the file to disk — a much more direct "a real save
    // just happened" signal than piggybacking on Write(), which GH also
    // calls for every undo checkpoint and copy/paste, and which
    // LogWriteSummary intentionally silences when the content hasn't
    // changed since the last call. This logs on EVERY save regardless of
    // content, which is the point: confirming the save reached this
    // component at all, even when nothing was captured/changed.
    private static void OnDocModifiedChanged(object sender, Grasshopper.Kernel.GH_DocModifiedEventArgs e)
    {
        if (e.Modified) return; // only the dirty→clean transition means "just saved"
        if (_restoringModified) return; // our own ScheduleLogFlush restoring IsModified after the fact — not a real save
        var state = GetSerializedState(e.Document);
        _lastSavedAt[e.Document] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Log(state == null
            ? "Saved (no captured UI for this document)."
            : $"Saved: {SummarizeStateCounts(state)}.");
    }

    // Catches color changes made directly on the canvas (right-click the
    // picker), not just ones made through Slate.
    private static void PushColourPickerUpdates()
    {
        var win = _instance;
        if (win == null) return;
        foreach (var kv in win._colourPickers)
        {
            var name = kv.Value.NickName;
            var hex  = ColorToHex(kv.Value.SwatchColour);
            var fingerprint = name + hex;
            if (_lastPushedColours.TryGetValue(kv.Key, out var last) && last == fingerprint) continue;
            _lastPushedColours[kv.Key] = fingerprint;

            win.PostToJs(System.Text.Json.JsonSerializer.Serialize(new {
                type = "colourPicker_update",
                id = kv.Key,
                name,
                value = hex
            }));
        }
    }

    // Catches Interval/LockTargets changes made directly on the canvas (the
    // native ModeBox/LockBox, or the right-click Interval submenu) — same
    // reasoning as PushColourPickerUpdates. Only fires on the next solve
    // anywhere in the doc, same caveat as that one: a Trigger edit alone
    // doesn't itself schedule a solve.
    private static void PushTriggerUpdates()
    {
        var win = _instance;
        if (win == null) return;
        foreach (var kv in win._triggers)
        {
            var name = kv.Value.NickName;
            var interval = kv.Value.Interval;
            var intervalString = kv.Value.IntervalString;
            var lockTargets = kv.Value.LockTargets;
            var fingerprint = name + "" + interval + "" + intervalString + "" + lockTargets;
            if (_lastPushedTriggers.TryGetValue(kv.Key, out var last) && last == fingerprint) continue;
            _lastPushedTriggers[kv.Key] = fingerprint;

            win.PostToJs(System.Text.Json.JsonSerializer.Serialize(new {
                type = "trigger_update",
                id = kv.Key,
                name,
                interval,
                intervalString,
                lockTargets
            }));
        }
    }

    // These pushes run on EVERY document solve — anywhere in the canvas,
    // not just Slate-related changes — across every tracked object, over every
    // workspace. Left unguarded that's O(tracked objects) wasted messages (each
    // triggering a full workspace-tree rebuild in JS) per solve, even when
    // nothing actually changed. Caching the last-pushed fingerprint per id and
    // skipping unchanged ones cuts that down to just real changes.
    private static readonly Dictionary<string, string> _lastPushedNames   = new();
    private static readonly Dictionary<string, string> _lastPushedPanels = new();
    private static readonly Dictionary<string, string> _lastPushedPickers = new();
    private static readonly Dictionary<string, string> _lastPushedHumanLists = new();
    private static readonly Dictionary<string, string> _lastPushedColours = new();
    private static readonly Dictionary<string, string> _lastPushedTriggers = new();

    private static void ClearPushCaches()
    {
        _lastPushedNames.Clear();
        _lastPushedPanels.Clear();
        _lastPushedHumanLists.Clear();
        _lastPushedColours.Clear();
        _lastPushedPickers.Clear();
        _lastPushedTriggers.Clear();
    }

    private static void PushSliderNameUpdates()
    {
        var win = _instance;
        if (win == null) return;

        void PushIfChanged(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_lastPushedNames.TryGetValue(id, out var last) && last == name) return;
            _lastPushedNames[id] = name;
            win.PostToJs(System.Text.Json.JsonSerializer.Serialize(new {
                type = "slider_name_update",
                id,
                name
            }));
        }

        foreach (var kv in win._sliders)    PushIfChanged(kv.Key, kv.Value.ImpliedNickName);
        foreach (var kv in win._toggles)    PushIfChanged(kv.Key, kv.Value.NickName);
        foreach (var kv in win._buttons)    PushIfChanged(kv.Key, kv.Value.NickName);
        foreach (var kv in win._valueLists) PushIfChanged(kv.Key, kv.Value.NickName);
        foreach (var kv in win._pancakeTrueOnlyButtons) PushIfChanged(kv.Key, kv.Value.NickName);
    }

    // GH_Panel.UserText is only the typed-in source text for an unwired panel
    // (the string it feeds downstream). A wired panel never touches UserText —
    // GH_Param<T>.CollectData() copies the source's data straight into
    // VolatileData instead of running CollectVolatileData_Custom(), so the
    // canvas's own display (GH_PanelAttributes.GetContentAsString) reads
    // VolatileData once there's a source. Mirror that here, or a wired panel's
    // captured value always comes through empty.
    private static string GetPanelText(GH_Panel panel)
    {
        if (panel.SourceCount == 0) return panel.UserText;

        if (panel.VolatileData is Grasshopper.Kernel.Data.GH_Structure<Grasshopper.Kernel.Types.GH_String> data)
        {
            return string.Join("\n", data.AllData(true)
                .OfType<Grasshopper.Kernel.Types.GH_String>()
                .Select(g => g.Value ?? ""));
        }
        return "";
    }

    // Panels carry more than a name (text content + connected/editable state),
    // and that content can change every solve even without the user touching
    // Slate — so they get their own push, separate from the name-only sync.
    private static void PushPanelTextUpdates()
    {
        var win = _instance;
        if (win == null) return;
        foreach (var kv in win._panels)
        {
            var name     = kv.Value.NickName;
            var text     = GetPanelText(kv.Value);
            var readOnly = kv.Value.SourceCount > 0;
            var fingerprint = name + "" + text + "" + readOnly;
            if (_lastPushedPanels.TryGetValue(kv.Key, out var last) && last == fingerprint) continue;
            _lastPushedPanels[kv.Key] = fingerprint;

            win.PostToJs(System.Text.Json.JsonSerializer.Serialize(new {
                type = "panel_text_update",
                id = kv.Key,
                name,
                text,
                readOnly
            }));
        }
    }

    // DataList is null when the picker has nothing wired into it yet — every
    // read of it goes through here instead of crashing capture/sync silently.
    private static List<string> GetPickerOptions(GH_ItemPicker picker) =>
        picker.DataList?.Cast<object>().Select(o => o?.ToString() ?? "").ToList() ?? new List<string>();

    // The candidate list comes from a wired input, which can change on its own
    // between solves — so like panels, item pickers get their own push instead
    // of relying on the name-only sync.
    private static void PushItemPickerUpdates()
    {
        var win = _instance;
        if (win == null) return;
        foreach (var kv in win._itemPickers)
        {
            var options = GetPickerOptions(kv.Value);
            var name    = kv.Value.NickName;
            var index   = kv.Value.TreeIndex;
            var fingerprint = name + "" + index + "" + string.Join("", options);
            if (_lastPushedPickers.TryGetValue(kv.Key, out var last) && last == fingerprint) continue;
            _lastPushedPickers[kv.Key] = fingerprint;

            win.PostToJs(System.Text.Json.JsonSerializer.Serialize(new {
                type = "itemPicker_update",
                id = kv.Key,
                name,
                options,
                value = index
            }));
        }
    }

    // Human's "Item Selector" — same reasoning as item pickers: its list comes
    // from a wired input and can change between solves on its own.
    private static void PushHumanValueListUpdates()
    {
        var win = _instance;
        if (win == null) return;
        foreach (var kv in win._humanValueLists)
        {
            var (options, value, multi, cycle, loop) = GetHumanListItems(kv.Value);
            var name = kv.Value.NickName;
            var fingerprint = name + "" + multi + "" + JsonSerializer.Serialize(value) + "" + string.Join("", options);
            if (_lastPushedHumanLists.TryGetValue(kv.Key, out var last) && last == fingerprint) continue;
            _lastPushedHumanLists[kv.Key] = fingerprint;

            win.PostToJs(System.Text.Json.JsonSerializer.Serialize(new {
                type = "humanValueList_update",
                id = kv.Key,
                name,
                options,
                value,
                multiSelect = multi,
                cycle,
                loop
            }));
        }
    }

    private static void ApplyLatestValues()
    {
        var win = _instance;
        if (win == null) { _latestValues.Clear(); _latestColourValues.Clear(); _solveRunning = false; return; }

        foreach (var kv in _latestValues)
        {
            if (win._sliders.TryGetValue(kv.Key, out var s))
            {
                s.SetSliderValue((decimal)kv.Value);
                s.ExpireSolution(false);   // expire without scheduling
            }
            else if (win._toggles.TryGetValue(kv.Key, out var tgl))
            {
                tgl.Value = kv.Value != 0;
                tgl.ExpireSolution(false);
            }
            else if (win._buttons.TryGetValue(kv.Key, out var btn))
            {
                btn.ButtonDown = kv.Value != 0;
                btn.ExpireSolution(false);
            }
            else if (win._valueLists.TryGetValue(kv.Key, out var vl))
            {
                if (IsMultiSelectMode(vl.ListMode)) vl.ToggleItem((int)kv.Value);
                else                                vl.SelectItem((int)kv.Value);
                vl.ExpireSolution(false);
            }
            else if (win._itemPickers.TryGetValue(kv.Key, out var picker))
            {
                picker.TreeIndex = (int)kv.Value;
                picker.ExpireSolution(false);
            }
            else if (win._humanValueLists.TryGetValue(kv.Key, out var hvl))
            {
                SelectOrToggleHumanItem(hvl, (int)kv.Value);
                hvl.ExpireSolution(false);
            }
            else if (win._pancakeTrueOnlyButtons.TryGetValue(kv.Key, out var pbtn))
            {
                SetPancakeButtonDown(pbtn, kv.Value != 0);
                pbtn.ExpireSolution(false);
            }
        }

        foreach (var kv in _latestColourValues)
        {
            if (win._colourPickers.TryGetValue(kv.Key, out var cp))
            {
                cp.SwatchColour = HexToColor(kv.Value);
                cp.ExpireSolution(false);
            }
        }

        _latestValues.Clear();
        _latestColourValues.Clear();
        _hostDocument?.ScheduleSolution(1, null);
    }

    private static void Log(string msg)
    {
        LogQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {msg}");
        ScheduleLogFlush();
    }

    // Log() fires from async paths that run well after SlatePanel's own last
    // SolveInstance — RestoreState/dropped-item messages all arrive on
    // "ui_ready", after the file's initial load solve already
    // drained an (empty) queue to the "Log" output. Without this, every
    // diagnostic line sits in LogQueue until the user happens to flip an
    // input (Show, say), so the output looks silent even when something was
    // actually logged. ExpireSolution(true) forces exactly the recompute
    // that drains it; _logFlushScheduled coalesces a burst of Log() calls
    // (e.g. several dropped-item lines in one RestoreState) into one
    // recompute instead of one per line. Gated by HostComponent + BeginInvoke
    // (same marshalling SyncToDocument uses) since Log() can be called from
    // a WebView2 IPC callback.
    //
    // GH_Document.IsModified flips true on basically any forced recompute —
    // including this purely-diagnostic one, whose only job is refreshing the
    // "Log"/"State" outputs — so a save/open/write immediately followed by
    // this flush used to re-dirty the file the instant it was saved/opened.
    // Snapshotting IsModified and restoring it after the recompute fixes
    // that, but assigning IsModified re-raises ModifiedChanged synchronously
    // — without _restoringModified to swallow that synthetic re-entry,
    // OnDocModifiedChanged would see another dirty→clean transition, log
    // again, schedule another flush, and loop forever (this crashed
    // Grasshopper once already). The guard makes the restore's own event a
    // no-op so only the real save's transition ever does anything — this is
    // trigger-agnostic (doesn't matter if the flush came from a real save or
    // a Debug-logging trace), so it should hold with Debug logging on too,
    // though that combination hasn't been stress-tested.
    private static bool _logFlushScheduled;
    private static bool _restoringModified;
    private static void ScheduleLogFlush()
    {
        var win = _instance;
        if (win == null || win.IsDisposed || _logFlushScheduled) return;
        _logFlushScheduled = true;
        win.BeginInvoke((Action)(() =>
        {
            _logFlushScheduled = false;
            var doc = HostDocument;
            bool wasModified = doc?.IsModified ?? false;
            HostComponent?.ExpireSolution(true);
            if (doc != null && doc.IsModified != wasModified)
            {
                _restoringModified = true;
                doc.IsModified = wasModified;
                _restoringModified = false;
            }
        }));
    }

    // ── singleton ────────────────────────────────────────────────────────────

    public static SlateWindow GetOrCreate()
    {
        if (_instance == null || _instance.IsDisposed)
            _instance = new SlateWindow();
        return _instance;
    }

    // Which document's geometry was last reconciled into the live window —
    // lets EnsureVisible tell "first time this document is being shown/
    // switched to" (reconcile) apart from "still the same document, just
    // another SolveInstance re-run while already showing" (skip, so it
    // doesn't fight a live user resize/maximize of the window they're
    // currently looking at).
    private static Grasshopper.Kernel.GH_Document? _lastGeometrySyncedDoc;

    public static void EnsureVisible()
    {
        var w = GetOrCreate();

        bool wasVisible = w.Visible;
        if (!wasVisible) w.Show();
        w.BringToFront();

        // Reconcile whenever the window is being (re)shown for a document it
        // wasn't just showing a moment ago — covers both "was hidden, now
        // shown" and "was already visible but for a DIFFERENT document" (a
        // tab switch while the singleton stays on screen). The latter matters
        // just as much: applying Size/Location/WindowState to an already-
        // visible window is what reliably sticks (see
        // pencere-boyutu-maximize.md — doing it while still hidden left a
        // singleton that had been Maximized for a previous document visually
        // stuck maximized), so skipping this on a same-document re-run isn't
        // about reliability, only about not undoing a resize/maximize the
        // user is actively doing to the window right now.
        //
        // Falls back through: this session's Pending* (just Read() from a
        // freshly opened file) → this document's cached geometry (already
        // seen this session, e.g. switching back to it) → the class defaults
        // (brand new document, never saved).
        if (!wasVisible || HostDocument != _lastGeometrySyncedDoc)
        {
            var size     = PendingWindowSize     ?? (HostDocument != null && _sizeByDoc.TryGetValue(HostDocument, out var sz)     ? sz : DefaultWindowSize);
            var location = PendingWindowLocation ?? (HostDocument != null && _locationByDoc.TryGetValue(HostDocument, out var loc) ? loc : DefaultWindowLocation);

            ApplyGeometry(w, size, location);
            if (HostDocument != null)
            {
                _sizeByDoc[HostDocument]     = w.Size;
                _locationByDoc[HostDocument] = w.Location;
            }
            _lastGeometrySyncedDoc = HostDocument;
            PendingWindowSize     = null;
            PendingWindowLocation = null;
        }

        // WinForms re-asserts its own (unset/default) Form.Icon at various
        // points around Show()/activation — same class of "Windows quietly
        // resets our custom window state" issue as TopMost below in
        // InitWebViewAsync. Reassert ours right after Show() wins that race
        // instead of losing to it (this is what showed Windows' generic
        // no-icon glyph at startup instead of the brand mark).
        if (!wasVisible) w.ApplyTitleBarTheme(w._lastAppliedTheme != "light");
    }

    public static void HideIfOpen()
    {
        if (_instance != null && !_instance.IsDisposed && _instance.Visible)
            _instance.Hide();
    }

    // ── active-tab tracking ──────────────────────────────────────────────────
    // Grasshopper can have several .gh files open as tabs on ONE canvas —
    // AddedToDocument/RemovedFromDocument (see SlatePanel) only fire when a
    // Slate panel is placed/deleted, never when the user just clicks a
    // different already-open tab, so HostDocument used to keep pointing at
    // whichever file's panel loaded/solved LAST, not whichever tab is actually
    // on screen. Hooked onto GH_Canvas.DocumentChanged from
    // SlateAssemblyPriority so every tab switch re-syncs which document (if
    // any) the window is currently showing.
    // Separate from HostDocument, which deliberately keeps pointing at the last
    // document that HAD a panel even while a panel-less tab is active (so that
    // tab's state/live object refs survive the detour) — de-duping purely on
    // HostDocument would then make switching BACK to that same document a
    // no-op and skip re-showing the window. This tracks whatever tab is
    // literally on screen right now, independent of that.
    private static Grasshopper.Kernel.GH_Document? _lastActiveDoc;

    // Called from SlateAssemblyPriority on GH_DocumentServer.DocumentRemoved —
    // fired when a .gh file is actually closed, not when a component is merely
    // deleted from a still-open document (RemovedFromDocument, which
    // deliberately keeps this document's cache around so Undo can restore it).
    // A closed document's GH_Document instance can never come back, so nothing
    // is lost by dropping every reference to it here — and since those
    // references (the per-doc dictionaries below, plus HostDocument/
    // HostComponent/_lastActiveDoc while they still point at it) are the only
    // thing keeping the file's whole object graph (sliders, panels, wires)
    // alive, this is what lets it actually get garbage collected instead of
    // leaking for the rest of the Rhino session.
    //
    // Also hides the window itself when this document was the one being
    // shown (confirmed live, 2026-09-01, see multi-window-state-sync.md):
    // SlatePanel.RemovedFromDocument has its own guard for this
    // (HostDocument != document → skip, meant for "closing a background
    // tab while the canvas already switched focus to another one" — don't
    // re-hide a window that's now correctly showing THAT document). But
    // DocumentRemoved and RemovedFromDocument fire independently, and if
    // this method runs first and nulls HostDocument below, that guard then
    // sees HostDocument(null) != document and wrongly concludes some other
    // document must already be active, skipping its own hide — leaving the
    // window orphaned, still open, showing whatever was last rendered.
    // Hiding here too makes the outcome correct regardless of which of the
    // two runs first: this check only fires when `doc` was truly the one
    // being shown (the same condition RemovedFromDocument's guard already
    // requires to act), so the background-tab case above is untouched —
    // HostDocument is already the OTHER (still-open) document by the time
    // either runs, and this stays a no-op for it.
    public static void EvictDocument(Grasshopper.Kernel.GH_Document doc)
    {
        _uiStateByDoc.Remove(doc);
        _sizeByDoc.Remove(doc);
        _locationByDoc.Remove(doc);
        if (_lastActiveDoc == doc) _lastActiveDoc = null;
        if (_lastGeometrySyncedDoc == doc) _lastGeometrySyncedDoc = null;
        if (HostComponent != null && HostComponent.OnPingDocument() == doc) HostComponent = null;
        if (HostDocument == doc)
        {
            HideIfOpen();
            HostDocument = null;
        }
    }

    public static void OnActiveDocumentChanged(Grasshopper.Kernel.GH_Document? newDoc)
    {
        if (newDoc == _lastActiveDoc) return;
        _lastActiveDoc = newDoc;

        var panel = newDoc?.Objects.OfType<Slate.Components.SlatePanel>().FirstOrDefault();
        if (panel == null)
        {
            // The newly active file has no Slate panel at all — per-file
            // opt-in, so nothing of THIS file's business is shown for it.
            // HostDocument is deliberately left pointing at the last file
            // that DID have a panel, so its state/live object refs are still
            // intact if the user switches back to it.
            HideIfOpen();
            return;
        }

        SyncToDocument(newDoc!); // non-null here: panel is derived from newDoc and already returned above if null

        if (panel.LastShowValue) EnsureVisible();
        else                     HideIfOpen();
    }

    // ── construction ─────────────────────────────────────────────────────────

    internal static readonly Size  DefaultWindowSize     = new Size(380, 760);
    internal static readonly Point DefaultWindowLocation = new Point(60, 60);

    private SlateWindow()
    {
        Text            = "Slate";
        FormBorderStyle = FormBorderStyle.Sizable;
        Size            = _lastKnownSize     ?? DefaultWindowSize;
        MinimumSize     = new Size(320, 480);
        StartPosition   = FormStartPosition.Manual;
        Location        = _lastKnownLocation ?? DefaultWindowLocation;
        BackColor       = Color.FromArgb(18, 18, 18);

        // Diagnostic (2026-09-14): a save made while genuinely maximized was
        // observed writing an inflated Size (matching live maximized bounds)
        // alongside an UNCHANGED, correct Location (still the pre-maximize
        // value) — see window-geometry investigation. That split only makes
        // sense if Resize and Move disagree about WindowState at the moment
        // each fires during the same transition. Logging both raw here to
        // catch it live instead of guessing further.
        Resize += (_, _) =>
        {
            DebugLog($"Resize event: WindowState={WindowState} Size={Size} (doc={HostDocument?.DisplayName})");
            if (WindowState == FormWindowState.Normal) { _lastKnownSize     = Size;     if (HostDocument != null) _sizeByDoc[HostDocument]     = Size; }
        };
        Move   += (_, _) =>
        {
            DebugLog($"Move event: WindowState={WindowState} Location={Location} (doc={HostDocument?.DisplayName})");
            if (WindowState == FormWindowState.Normal) { _lastKnownLocation = Location; if (HostDocument != null) _locationByDoc[HostDocument] = Location; }
        };

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Load += async (_, _) =>
        {
            try   { await InitWebViewAsync(); }
            catch (Exception ex) { ShowError(ex.Message); }
        };

        // Started here (not gated on WebView2 being ready) since PostToJs
        // already no-ops safely until CoreWebView2 exists.
        _altPollTimer.Tick += (_, _) =>
        {
            bool held = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            if (held != _altHeld)
            {
                _altHeld = held;
                PostToJs(SlateEvent.AltStateChanged(held));
            }

            bool mouseOverWindow = Visible && Bounds.Contains(Cursor.Position);
            bool cDown = (GetAsyncKeyState(VK_C) & 0x8000) != 0;
            bool xDown = (GetAsyncKeyState(VK_X) & 0x8000) != 0;
            bool gDown = (GetAsyncKeyState(VK_G) & 0x8000) != 0;
            if (cDown && !_cHeld && mouseOverWindow) PostToJs(SlateEvent.CaptureHotkey());
            if (xDown && !_xHeld && mouseOverWindow) PostToJs(SlateEvent.DeleteHotkey());
            if (gDown && !_gHeld && mouseOverWindow) PostToJs(SlateEvent.GroupHotkey());
            _cHeld = cDown;
            _xHeld = xDown;
            _gHeld = gDown;
        };
        _altPollTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _altPollTimer.Dispose();
        base.Dispose(disposing);
    }

    private void ShowError(string msg)
    {
        _webView.Visible = false;
        var lbl = new Label
        {
            Text      = "Slate: WebView2 init failed.\n\n" + msg,
            Dock      = DockStyle.Fill,
            ForeColor = Color.FromArgb(220, 80, 80),
            BackColor = Color.FromArgb(18, 18, 18),
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 9f),
            Padding   = new Padding(16),
        };
        Controls.Add(lbl);
    }

    // ── WebView2 init ─────────────────────────────────────────────────────────

    private async Task InitWebViewAsync()
    {
        // LocalAppData, not Temp — Temp is fair game for disk-cleanup tools, which
        // would silently wipe the WebView2 profile (and anything Slate stores in it,
        // like localStorage) out from under a running Rhino session.
        string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slate", "WebView2");

        // The embedded UI is served over https://slate.local/... via
        // SetVirtualHostNameToFolderMapping below, and Vite emits fixed
        // filenames (assets/slate.js, assets/slate.css — no content hash, see
        // vite.config.js) so the URL never changes between builds. Chromium's
        // disk cache in the persistent userDataFolder above will happily keep
        // serving an old build under that same URL across Rhino restarts —
        // capping the disk cache effectively disables it, so every reload
        // actually picks up what GetOrExtractWebRoot() just wrote to disk.
        var envOptions = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disk-cache-size=1" };
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled            = true;

        _webView.CoreWebView2.WebMessageReceived += OnMessageFromJs;

        // Ctrl+scroll zoom is WebView2's own default (Chromium) behaviour — not
        // something we wired up — but the UI has no way to show the current
        // percentage without this. Fires as the user scrolls, and once more on
        // release, so the status-bar hint can track it live.
        _webView.ZoomFactorChanged += (_, _) =>
            PostToJs(SlateEvent.ZoomChanged(_webView.ZoomFactor));

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "slate.local", GetOrExtractWebRoot(), CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Navigate("https://slate.local/index.html");

        // Re-assert always-on-top and the titlebar icon after WebView2 init —
        // both can get reset by window flag/handle churn during init.
        SetPin(true);
        ApplyTitleBarTheme(_lastAppliedTheme != "light");
    }

    private void SetPin(bool pin)
    {
        TopMost = pin;
        SetWindowPos(Handle,
            pin ? HWND_TOPMOST : HWND_NOTOPMOST,
            0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    // ── JS → C# messages ─────────────────────────────────────────────────────

    private void OnMessageFromJs(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc  = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";

            switch (type)
            {
                case "slider_change":
                    string sid   = root.GetProperty("id").GetString()    ?? "";
                    double value = root.GetProperty("value").GetDouble();
                    if (_sliders.ContainsKey(sid) || _toggles.ContainsKey(sid) || _buttons.ContainsKey(sid) || _valueLists.ContainsKey(sid) || _itemPickers.ContainsKey(sid) || _humanValueLists.ContainsKey(sid) || _pancakeTrueOnlyButtons.ContainsKey(sid))
                    {
                        _latestValues[sid] = value;   // always overwrite — only last value survives
                        if (!_solveRunning) { _solveRunning = true; ApplyLatestValues(); }
                    }
                    break;

                case "panel_change":
                    string pid  = root.GetProperty("id").GetString() ?? "";
                    string text = root.GetProperty("value").GetString() ?? "";
                    if (_panels.TryGetValue(pid, out var panel))
                    {
                        panel.SetUserText(text);
                        panel.ExpireSolution(true);
                    }
                    break;

                case "colour_change":
                    string cid = root.GetProperty("id").GetString() ?? "";
                    string hex = root.GetProperty("value").GetString() ?? "";
                    if (_colourPickers.ContainsKey(cid))
                    {
                        _latestColourValues[cid] = hex;   // same latest-value batching as slider_change
                        if (!_solveRunning) { _solveRunning = true; ApplyLatestValues(); }
                    }
                    break;

                // Interval/lock are config, not solve data, but still need
                // ExpireSolution(true) (same immediate-recompute idiom as
                // panel_change above) — without it neither the Trigger's own
                // canvas widget (ModeBox/LockBox/interval text) nor anything
                // downstream redraws until some unrelated solve happens to
                // pass through.
                case "trigger_interval_change":
                    string tiid = root.GetProperty("id").GetString() ?? "";
                    int interval = root.GetProperty("value").GetInt32();
                    if (_triggers.TryGetValue(tiid, out var trInterval))
                    {
                        trInterval.Interval = interval;
                        trInterval.ExpireSolution(true);
                    }
                    break;

                case "trigger_lock_change":
                    string tlid = root.GetProperty("id").GetString() ?? "";
                    bool lockTargets = root.GetProperty("value").GetBoolean();
                    if (_triggers.TryGetValue(tlid, out var trLock))
                    {
                        trLock.LockTargets = lockTargets;
                        trLock.ExpireSolution(true);
                    }
                    break;

                // Mirrors clicking the native PlayBox — ExpireTargets() marks the
                // wired targets expired, ExpireSolution(true) on the timer itself
                // forces the actual recompute now (same idiom as panel_change,
                // not the batched _latestValues/ScheduleSolution path).
                case "trigger_fire":
                    string tfid = root.GetProperty("id").GetString() ?? "";
                    if (_triggers.TryGetValue(tfid, out var trFire))
                    {
                        trFire.ExpireTargets();
                        trFire.ExpireSolution(true);
                    }
                    break;

                case "pin":
                    bool pinned = root.GetProperty("value").GetBoolean();
                    Invoke(() => SetPin(pinned));
                    break;

                case "capture":
                    string tabId = root.TryGetProperty("tabId", out var t) ? t.GetString() ?? "main" : "main";
                    string? groupId = root.TryGetProperty("groupId", out var g) ? g.GetString() : null;
                    CaptureSelected(tabId, groupId);
                    break;

                // Pane's right-click "Sort: Canvas Position" — JS never stores GH
                // pivots itself (would bloat every saved .gh's ui_state), so it
                // asks for them live here, once, for exactly the ids it needs.
                case "sort_positions_request":
                {
                    string sortTabId = root.TryGetProperty("tabId", out var stid) ? stid.GetString() ?? "" : "";
                    var sortIds = new List<string>();
                    if (root.TryGetProperty("ids", out var idsEl))
                        foreach (var idEl in idsEl.EnumerateArray())
                            sortIds.Add(idEl.GetString() ?? "");

                    Invoke(() =>
                    {
                        var positions = new Dictionary<string, float[]>();
                        foreach (var sortId in sortIds)
                            if (TryGetLivePivot(sortId, out var pivot))
                                positions[sortId] = new[] { pivot.X, pivot.Y };
                        PostToJs(SlateEvent.SortPositionsResult(sortTabId, positions));
                    });
                    break;
                }

                case "ui_ready":
                    // Page (re)loaded — restore state if available
                    if (NeedsFileRestore && PendingFileState is string fs && HostDocument is Grasshopper.Kernel.GH_Document fd)
                    {
                        RestoreState(fs, fd);
                        NeedsFileRestore = false;
                        PendingFileState = null;
                    }
                    else if (!NeedsFileRestore && HostDocument is Grasshopper.Kernel.GH_Document sd
                             && _uiStateByDoc.TryGetValue(sd, out var ss))
                    {
                        RestoreState(ss, sd);
                    }
                    break;

                case "state_snapshot":
                    // Used to be guarded by an epoch check here (rejecting a
                    // snapshot generated before the most recent SyncToDocument,
                    // to stop a stale resize-debounce timer from re-poisoning
                    // HostDocument's cache right after ClearAll/RestoreState
                    // just set it correctly). Removed 2026-09-07: that guard
                    // never had confirmed evidence of fixing the cross-document
                    // case it targeted, and it was provably discarding
                    // legitimate captures made in the gap between a document
                    // switch and its deferred restore. SyncToDocument now closes
                    // that gap directly (synchronous ResetAll/tab-switch, only
                    // RestoreState-with-cached-state defers), so every
                    // snapshot that arrives here is trusted for whatever
                    // HostDocument currently is.
                    DebugLog($"state_snapshot accepted for doc={HostDocument?.DisplayName ?? "null"}: {SummarizeStateCounts(e.WebMessageAsJson)}.");
                    if (HostDocument != null) _uiStateByDoc[HostDocument] = e.WebMessageAsJson;
                    string themeName = root.TryGetProperty("theme", out var th) ? th.GetString() ?? "dark" : "dark";
                    if (themeName != _lastAppliedTheme)
                    {
                        _lastAppliedTheme = themeName;
                        ApplyTitleBarTheme(themeName != "light");
                    }
                    break;
            }
        }
        catch (Exception ex) { Log($"OnMessageFromJs error: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void CaptureSelected(string tabId, string? groupId = null)
    {
        var doc = HostDocument
               ?? Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null) return;

        var selected = doc.Objects.Where(o => o.Attributes?.Selected == true).ToList();

        var sliders = selected.OfType<GH_NumberSlider>().ToList();
        foreach (var s in sliders)
            AddSlider(tabId, groupId, s);

        var toggles = selected.OfType<GH_BooleanToggle>().ToList();
        foreach (var t in toggles)
            AddToggle(tabId, groupId, t);

        var buttons = selected.OfType<GH_ButtonObject>().ToList();
        foreach (var b in buttons)
            AddButton(tabId, groupId, b);

        var valueLists = selected.OfType<GH_ValueList>().ToList();
        foreach (var v in valueLists)
            AddValueList(tabId, groupId, v);

        var panels = selected.OfType<GH_Panel>().ToList();
        foreach (var p in panels)
            AddPanel(tabId, groupId, p);

        var itemPickers = selected.OfType<GH_ItemPicker>().ToList();
        foreach (var ip in itemPickers)
            AddItemPicker(tabId, groupId, ip);

        EnsureHumanReflection();
        var humanLists = _humanValueListType != null
            ? selected.Where(o => _humanValueListType.IsInstanceOfType(o)).ToList()
            : new List<IGH_DocumentObject>();
        foreach (var h in humanLists)
            if (h is IGH_Param hp) AddHumanValueList(tabId, groupId, hp);

        var colourPickers = selected.OfType<GH_ColourSwatch>().ToList();
        foreach (var c in colourPickers)
            AddColourPicker(tabId, groupId, c);

        EnsurePancakeReflection();
        var pancakeButtons = _pancakeTrueOnlyBtnType != null
            ? selected.Where(o => _pancakeTrueOnlyBtnType.IsInstanceOfType(o)).ToList()
            : new List<IGH_DocumentObject>();
        foreach (var pb in pancakeButtons)
            if (pb is IGH_Param pbp) AddPancakeTrueOnlyButton(tabId, groupId, pbp);

        var triggers = selected.OfType<GH_Timer>().ToList();
        foreach (var tr in triggers)
            AddTrigger(tabId, groupId, tr);

        Log($"Captured {sliders.Count} slider(s), {toggles.Count} toggle(s), {buttons.Count} button(s), {valueLists.Count} value list(s), {panels.Count} panel(s), {itemPickers.Count} item picker(s), {humanLists.Count} item selector(s), {colourPickers.Count} colour picker(s), {pancakeButtons.Count} true-only button(s), {triggers.Count} trigger(s).");
    }

    // ── C# → JS ──────────────────────────────────────────────────────────────

    private void PostToJs(string json)
    {
        if (_webView.CoreWebView2 == null) return;
        _webView.CoreWebView2.PostWebMessageAsString(json);
    }

    // ── public API ────────────────────────────────────────────────────────────

    // GH_SliderBase.DecimalPlaces is only meaningful in Float mode — GH doesn't
    // reset it when a slider is switched to Integer/Even/Odd, so it can carry a
    // stale nonzero value (e.g. left over from before the mode was changed).
    static int EffectiveDecimalPlaces(GH_SliderBase slider) =>
        slider.Type == GH_SliderAccuracy.Float ? slider.DecimalPlaces : 0;

    public void AddSlider(string tabId, GH_NumberSlider slider) =>
        AddSlider(tabId, null, slider);

    // ImpliedNickName, not NickName: an unrenamed slider's NickName is empty,
    // but the canvas shows the nearest wired recipient's name in its place
    // (GH_NumberSlider.ImpliedNickName falls back to that automatically) — a
    // plain NickName read here would just show blank for those.
    public void AddSlider(string tabId, string? groupId, GH_NumberSlider slider)
    {
        string id = slider.InstanceGuid.ToString();
        _sliders[id] = slider;

        PostToJs(SlateEvent.SliderAdded(
            tabId,
            id,
            slider.ImpliedNickName,
            (double)slider.Slider.Minimum,
            (double)slider.Slider.Maximum,
            (double)slider.CurrentValue,
            EffectiveDecimalPlaces(slider.Slider),
            groupId));
    }

    public void AddToggle(string tabId, string? groupId, GH_BooleanToggle toggle)
    {
        string id = toggle.InstanceGuid.ToString();
        _toggles[id] = toggle;

        PostToJs(SlateEvent.ToggleAdded(tabId, id, toggle.NickName, toggle.Value, groupId));
    }

    public void AddButton(string tabId, string? groupId, GH_ButtonObject button)
    {
        string id = button.InstanceGuid.ToString();
        _buttons[id] = button;

        PostToJs(SlateEvent.ButtonAdded(tabId, id, button.NickName, button.ButtonDown, groupId));
    }

    // Only CheckList allows more than one item checked at once (ToggleItem,
    // value = array of indices). DropDown, Cycle AND Sequence are all
    // single-select (SelectItem, value = one index) — confirmed via reflection
    // on GH_ValueListAttributes: it has LayoutDropDown/LayoutCheckList/
    // LayoutSequence + RenderLeftArrow/RenderRightArrow/RenderSequence, but no
    // Cycle-specific layout/render at all, and Sequence's own render is the
    // "◀ value ▶" arrow widget (single current value, not a list of toggles)
    // — this used to be misclassified as multi-select, which is why a
    // Sequence-mode value list (what GH itself calls "Value Sequence" in that
    // arrow-widget form) captured into Slate as a checklist instead of
    // matching its own on-canvas look. See yapilacaklar/value-sequence.md.
    private static bool IsMultiSelectMode(GH_ValueListMode mode) =>
        mode == GH_ValueListMode.CheckList;

    // Cycle and Sequence both render as Slate's single-value "cycle" widget
    // (current item + prev/next) rather than a dropdown — Sequence's click
    // order (what makes it a "sequence" downstream) is preserved by clicking
    // through Slate's own cycle button the same way GH's arrows would.
    private static bool IsCycleMode(GH_ValueListMode mode) =>
        mode == GH_ValueListMode.Cycle || mode == GH_ValueListMode.Sequence;

    // Cycle wraps at the ends; Sequence stops there (matches GH's own arrow
    // widget — Sequence's arrows disable/no-op past the last item).
    private static bool IsLoopMode(GH_ValueListMode mode) =>
        mode == GH_ValueListMode.Cycle;

    // For CheckList, item order carries no meaning, so scanning ListItems top-to-bottom
    // is fine. Sequence's whole point is the CLICK order (its downstream value depends
    // on it), which ListItems can't give us — SelectedItems is GH's own order-preserving
    // record of that, so use it for both and let Sequence come out right.
    private static (object value, bool multi) GetValueListSelection(GH_ValueList valueList)
    {
        bool multi = IsMultiSelectMode(valueList.ListMode);
        if (multi)
        {
            var items = valueList.ListItems;
            var indices = valueList.SelectedItems.Select(si => items.IndexOf(si)).Where(i => i >= 0).ToList();
            return (indices, true);
        }
        return (valueList.ListItems.FindIndex(li => li.Selected), false);
    }

    public void AddValueList(string tabId, string? groupId, GH_ValueList valueList)
    {
        string id = valueList.InstanceGuid.ToString();
        _valueLists[id] = valueList;

        var options = valueList.ListItems.Select(i => i.Name);
        var (value, multi) = GetValueListSelection(valueList);
        PostToJs(SlateEvent.ValueListAdded(tabId, id, valueList.NickName, options, value, multi, IsCycleMode(valueList.ListMode), IsLoopMode(valueList.ListMode), groupId));
    }

    public void AddPanel(string tabId, string? groupId, GH_Panel panel)
    {
        string id = panel.InstanceGuid.ToString();
        _panels[id] = panel;

        PostToJs(SlateEvent.PanelAdded(tabId, id, panel.NickName, GetPanelText(panel), panel.SourceCount > 0, groupId));
    }

    public void AddItemPicker(string tabId, string? groupId, GH_ItemPicker picker)
    {
        string id = picker.InstanceGuid.ToString();
        _itemPickers[id] = picker;

        var options = GetPickerOptions(picker);
        PostToJs(SlateEvent.ItemPickerAdded(tabId, id, picker.NickName, options, picker.TreeIndex, groupId));
    }

    public void AddHumanValueList(string tabId, string? groupId, IGH_Param humanValueList)
    {
        string id = humanValueList.InstanceGuid.ToString();
        _humanValueLists[id] = humanValueList;

        var (options, value, multi, cycle, loop) = GetHumanListItems(humanValueList);
        PostToJs(SlateEvent.HumanValueListAdded(tabId, id, humanValueList.NickName, options, value, multi, cycle, loop, groupId));
    }

    public void AddColourPicker(string tabId, string? groupId, GH_ColourSwatch picker)
    {
        string id = picker.InstanceGuid.ToString();
        _colourPickers[id] = picker;

        PostToJs(SlateEvent.ColourPickerAdded(tabId, id, picker.NickName, ColorToHex(picker.SwatchColour), groupId));
    }

    public void AddPancakeTrueOnlyButton(string tabId, string? groupId, IGH_Param button)
    {
        string id = button.InstanceGuid.ToString();
        _pancakeTrueOnlyButtons[id] = button;

        PostToJs(SlateEvent.PancakeButtonAdded(tabId, id, button.NickName, GetPancakeButtonDown(button), groupId));
    }

    public void AddTrigger(string tabId, string? groupId, GH_Timer timer)
    {
        string id = timer.InstanceGuid.ToString();
        _triggers[id] = timer;

        PostToJs(SlateEvent.TriggerAdded(tabId, id, timer.NickName, timer.Interval, timer.IntervalString, timer.LockTargets, groupId));
    }

    public void ClearAll()
    {
        ClearDicts();
        PostToJs(SlateEvent.Cleared());
    }

    // Full structural reset — back to one empty workspace/pane/tab, as if the
    // panel had never captured anything. Clear only empties sliders/groups and
    // leaves the tab/pane/workspace layout as-is; this also drops that layout.
    public void ResetAll()
    {
        ClearDicts();
        PostToJs(SlateEvent.Reset());
    }

    // Everything ResetAll() does, plus the window goes back to its
    // as-if-never-opened size — unlike ResetAll(), which leaves the window
    // where the user put it. Location is left untouched (WinForms keeps the
    // top-left corner fixed and grows/shrinks Size from there), so the
    // window resets in place instead of jumping to a fixed corner every time.
    public void HardResetAll()
    {
        ResetAll();
        Size = DefaultWindowSize;
    }

    // Drops references to captured GH objects without telling JS anything
    // changed — used when the window is being handed off to a different
    // document (see HostDocument's setter), where the JS side is about to
    // get a fresh ui_ready/restore of its own instead of a "cleared" wipe.
    internal void ClearDicts()
    {
        _sliders.Clear();
        _toggles.Clear();
        _buttons.Clear();
        _valueLists.Clear();
        _panels.Clear();
        _itemPickers.Clear();
        _humanValueLists.Clear();
        _colourPickers.Clear();
        _pancakeTrueOnlyButtons.Clear();
        _triggers.Clear();
        ClearPushCaches();
    }

    public void RestoreState(string stateJson, Grasshopper.Kernel.GH_Document doc)
    {
        try
        {
            var state = JsonNode.Parse(stateJson)!.AsObject();

            var docSliders = doc.Objects
                .OfType<GH_NumberSlider>()
                .ToDictionary(s => s.InstanceGuid.ToString(), s => s);
            var docToggles = doc.Objects
                .OfType<GH_BooleanToggle>()
                .ToDictionary(t => t.InstanceGuid.ToString(), t => t);
            var docButtons = doc.Objects
                .OfType<GH_ButtonObject>()
                .ToDictionary(b => b.InstanceGuid.ToString(), b => b);
            var docValueLists = doc.Objects
                .OfType<GH_ValueList>()
                .ToDictionary(v => v.InstanceGuid.ToString(), v => v);
            var docPanels = doc.Objects
                .OfType<GH_Panel>()
                .ToDictionary(p => p.InstanceGuid.ToString(), p => p);
            var docItemPickers = doc.Objects
                .OfType<GH_ItemPicker>()
                .ToDictionary(p => p.InstanceGuid.ToString(), p => p);
            EnsureHumanReflection();
            var docHumanValueLists = (_humanValueListType != null
                    ? doc.Objects.Where(o => _humanValueListType.IsInstanceOfType(o)).OfType<IGH_Param>()
                    : Enumerable.Empty<IGH_Param>())
                .ToDictionary(p => p.InstanceGuid.ToString(), p => p);
            var docColourPickers = doc.Objects
                .OfType<GH_ColourSwatch>()
                .ToDictionary(c => c.InstanceGuid.ToString(), c => c);
            EnsurePancakeReflection();
            var docPancakeButtons = (_pancakeTrueOnlyBtnType != null
                    ? doc.Objects.Where(o => _pancakeTrueOnlyBtnType.IsInstanceOfType(o)).OfType<IGH_Param>()
                    : Enumerable.Empty<IGH_Param>())
                .ToDictionary(p => p.InstanceGuid.ToString(), p => p);
            var docTriggers = doc.Objects
                .OfType<GH_Timer>()
                .ToDictionary(t => t.InstanceGuid.ToString(), t => t);

            _sliders.Clear();
            _toggles.Clear();
            _buttons.Clear();
            _valueLists.Clear();
            _panels.Clear();
            _itemPickers.Clear();
            _humanValueLists.Clear();
            _colourPickers.Clear();
            _pancakeTrueOnlyButtons.Clear();
            _triggers.Clear();

            void ProcessItems(JsonArray arr)
            {
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    var s    = arr[i]!.AsObject();
                    var id   = s["id"]!.GetValue<string>();
                    var kind = s["type"]?.GetValue<string>() ?? "slider";

                    if (kind == "toggle")
                    {
                        if (docToggles.TryGetValue(id, out var tgl))
                        {
                            _toggles[id] = tgl;
                            s["value"] = tgl.Value ? 1 : 0;
                            s["name"]  = tgl.NickName;
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "button")
                    {
                        if (docButtons.TryGetValue(id, out var btn))
                        {
                            _buttons[id] = btn;
                            s["value"] = btn.ButtonDown ? 1 : 0;
                            s["name"]  = btn.NickName;
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "valueList")
                    {
                        if (docValueLists.TryGetValue(id, out var vl))
                        {
                            _valueLists[id] = vl;
                            var (value, multi) = GetValueListSelection(vl);
                            s["value"] = multi
                                ? new JsonArray(((List<int>)value).Select(v => JsonValue.Create(v)).ToArray())
                                : JsonValue.Create((int)value);
                            s["name"]        = vl.NickName;
                            s["multiSelect"] = multi;
                            s["cycle"]       = IsCycleMode(vl.ListMode);
                            s["loop"]        = IsLoopMode(vl.ListMode);
                            s["options"]     = new JsonArray(vl.ListItems.Select(li => JsonValue.Create(li.Name)).ToArray());
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "panel")
                    {
                        if (docPanels.TryGetValue(id, out var pnl))
                        {
                            _panels[id] = pnl;
                            s["value"]    = GetPanelText(pnl);
                            s["name"]     = pnl.NickName;
                            s["readOnly"] = pnl.SourceCount > 0;
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "itemPicker")
                    {
                        if (docItemPickers.TryGetValue(id, out var picker))
                        {
                            _itemPickers[id] = picker;
                            s["value"]   = picker.TreeIndex;
                            s["name"]    = picker.NickName;
                            s["options"] = new JsonArray(GetPickerOptions(picker).Select(o => JsonValue.Create(o)).ToArray());
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "humanValueList")
                    {
                        if (docHumanValueLists.TryGetValue(id, out var hvl))
                        {
                            _humanValueLists[id] = hvl;
                            var (options, value, multi, cycle, loop) = GetHumanListItems(hvl);
                            s["value"] = multi
                                ? new JsonArray(((List<int>)value).Select(v => JsonValue.Create(v)).ToArray())
                                : JsonValue.Create((int)value);
                            s["name"]        = hvl.NickName;
                            s["multiSelect"] = multi;
                            s["cycle"]       = cycle;
                            s["loop"]        = loop;
                            s["options"]     = new JsonArray(options.Select(o => JsonValue.Create(o)).ToArray());
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "colourPicker")
                    {
                        if (docColourPickers.TryGetValue(id, out var cp))
                        {
                            _colourPickers[id] = cp;
                            s["value"] = ColorToHex(cp.SwatchColour);
                            s["name"]  = cp.NickName;
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "pancakeButton")
                    {
                        if (docPancakeButtons.TryGetValue(id, out var pbtn))
                        {
                            _pancakeTrueOnlyButtons[id] = pbtn;
                            s["value"] = GetPancakeButtonDown(pbtn) ? 1 : 0;
                            s["name"]  = pbtn.NickName;
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (kind == "trigger")
                    {
                        if (docTriggers.TryGetValue(id, out var tr))
                        {
                            _triggers[id] = tr;
                            s["interval"]       = tr.Interval;
                            s["intervalString"] = tr.IntervalString;
                            s["lockTargets"]    = tr.LockTargets;
                            s["name"]           = tr.NickName;
                        }
                        else { Log($"RestoreState: dropped {kind} id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                    }
                    else if (docSliders.TryGetValue(id, out var gh))
                    {
                        _sliders[id] = gh;
                        s["value"]          = (double)gh.CurrentValue;
                        s["min"]            = (double)gh.Slider.Minimum;
                        s["max"]            = (double)gh.Slider.Maximum;
                        s["decimalPlaces"]  = EffectiveDecimalPlaces(gh.Slider);
                        s["name"]           = gh.ImpliedNickName;
                    }
                    else { Log($"RestoreState: dropped slider id={id} — no matching live object in doc.Objects."); arr.RemoveAt(i); }
                }
            }

            void ProcessTab(JsonObject tab)
            {
                ProcessItems(tab["sliders"]!.AsArray());
                foreach (var g in tab["groups"]!.AsArray())
                    ProcessItems(g!.AsObject()["sliders"]!.AsArray());
            }

            void ProcessNode(JsonObject node)
            {
                var type = node["type"]?.GetValue<string>();
                if (type == "leaf")
                {
                    foreach (var t in node["tabs"]!.AsArray())
                        ProcessTab(t!.AsObject());
                }
                else if (type == "split")
                {
                    ProcessNode(node["a"]!.AsObject());
                    ProcessNode(node["b"]!.AsObject());
                }
                else // legacy flat format: node IS the state object with "tabs" array
                {
                    if (node["tabs"] is JsonArray legacyTabs)
                        foreach (var t in legacyTabs)
                            ProcessTab(t!.AsObject());
                }
            }

            // Current format: state has "workspaces" (each with its own "layout" tree).
            // Older formats fall back gracefully: single "layout" key, or (oldest) the
            // state itself being the flat legacy tree with a "tabs" key.
            if (state["workspaces"] is JsonArray workspacesArr)
            {
                foreach (var w in workspacesArr)
                    if (w!.AsObject()["layout"] is JsonObject wLayout)
                        ProcessNode(wLayout);
            }
            else if (state["layout"] is JsonObject layoutNode)
                ProcessNode(layoutNode);
            else
                ProcessNode(state);

            // Old (pre-sizeA) split nodes only have a fractional "ratio" — converting
            // that to pixels needs the window size it was measured against. By this
            // point EnsureVisible() has already applied the saved win_w/win_h (clamped)
            // to the actual window, so the live size IS that reference size.
            state["winW"] = Width;
            state["winH"] = Height;

            // The size this geometry cycle was actually requested at, before
            // ApplyGeometry's screen-fit clamp (if any) shrank it — lets the
            // JS side tell "reopened on a smaller screen" apart from "reopened
            // at the same size" and rescale pane sizeA only when it actually
            // needs to. See ApplyGeometry's own comment.
            if (_lastRequestedWindowSize is Size requested)
            {
                state["savedWinW"] = requested.Width;
                state["savedWinH"] = requested.Height;
            }

            state["type"] = "restore_state";
            PostToJs(state.ToJsonString());
            var savedAt = state["savedAt"]?.GetValue<string>();
            Log($"State restored{(savedAt != null ? $" (file saved {savedAt})" : " (no save timestamp — older file)")}: {_sliders.Count} slider(s), {_toggles.Count} toggle(s), {_buttons.Count} button(s), {_valueLists.Count} value list(s), {_panels.Count} panel(s), {_itemPickers.Count} item picker(s), {_humanValueLists.Count} item selector(s), {_colourPickers.Count} colour picker(s), {_pancakeTrueOnlyButtons.Count} true-only button(s).");
        }
        catch (Exception ex) { Log($"RestoreState error: {ex.Message}"); }
    }

    // ── embedded resource helper ─────────────────────────────────────────────

    private static string? _webRoot;

    private static string GetOrExtractWebRoot()
    {
        if (_webRoot != null && Directory.Exists(_webRoot)) return _webRoot;

        string tmp = Path.Combine(Path.GetTempPath(), "SlateUI");
        Directory.CreateDirectory(tmp);

        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.Contains("Resources")) continue;

            // Convert resource name back to relative path
            // e.g. "Slate.Resources.assets.index-abc.js" → "assets/index-abc.js"
            string rel = name
                .Replace("Slate.Resources.", "")
                .Replace('.', Path.DirectorySeparatorChar);

            // Restore extension (last segment after last original dot)
            // Vite output: index.html, assets/index-Abc123.js etc.
            // ResourceName dots replace slashes, so we re-split on known extensions
            rel = FixResourcePath(name);

            string dest = Path.Combine(tmp, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            using var stream = asm.GetManifestResourceStream(name)!;
            using var file   = File.Create(dest);
            stream.CopyTo(file);
        }

        _webRoot = tmp;
        return tmp;
    }

    private static string FixResourcePath(string resourceName)
    {
        // Strip "Slate.Resources." prefix
        // (Substring, not the [x..] range operator — System.Range isn't in net48's mscorlib)
        string rel = resourceName.Substring("Slate.Resources.".Length);

        // Known web extensions — find the last occurrence to reconstruct the path
        string[] exts = { ".html", ".js", ".css", ".svg", ".png", ".ico", ".json" };
        foreach (var ext in exts)
        {
            string extKey = ext.TrimStart('.');
            int idx = rel.LastIndexOf('.' + extKey);
            if (idx < 0) continue;

            string withoutExt = rel.Substring(0, idx).Replace('.', Path.DirectorySeparatorChar);
            return withoutExt + ext;
        }

        return rel.Replace('.', Path.DirectorySeparatorChar);
    }
}
