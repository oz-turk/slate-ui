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

    // ── dark title bar (Windows 11) ─────────────────────────────────────────
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_BORDER_COLOR            = 34;
    const int DWMWA_CAPTION_COLOR           = 35;
    const int DWMWA_TEXT_COLOR              = 36;

    // COLORREF is 0x00BBGGRR, not RGB — build it from the palette's own tones
    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    void ApplyDarkTitleBar()
    {
        var caption = ColorRef(Color.FromArgb(26, 26, 26));   // matches app.css --bg
        var text    = ColorRef(Color.FromArgb(239, 239, 239)); // matches app.css --text
        var enabled = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
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
    private readonly Dictionary<string, GH_ColourPickerObject> _colourPickers = new();

    // Pancake plugin's "True Only Button" (Pancake.GH.Params.TrueOnlyBtn) — a
    // GH_Param subclass with the same ButtonDown shape as core GH_ButtonObject,
    // reflected rather than referenced at compile time so Slate still builds
    // and runs fine on a machine without Pancake installed.
    private readonly Dictionary<string, IGH_Param> _pancakeTrueOnlyButtons = new();

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return Color.FromArgb(r, g, b);
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

    private static (List<string> options, object value, bool multi) GetHumanListItems(object obj)
    {
        var options   = new List<string>();
        var selected  = new List<int>();
        var items = _humanValueListType?.GetProperty("ListItems")?.GetValue(obj) as System.Collections.IEnumerable;

        if (items != null)
        {
            PropertyInfo? nameProp = null, selProp = null;
            int i = 0;
            foreach (var item in items)
            {
                nameProp ??= item.GetType().GetProperty("Name");
                selProp  ??= item.GetType().GetProperty("Selected");
                options.Add(nameProp?.GetValue(item) as string ?? "");
                if (selProp?.GetValue(item) is true) selected.Add(i);
                i++;
            }
        }

        var mode  = GetHumanListMode(obj);
        bool multi = mode == "CheckList" || mode == "Sequence";
        object value = multi ? selected : (selected.Count > 0 ? selected[0] : -1);
        return (options, value, multi);
    }

    private static void SelectOrToggleHumanItem(object obj, int index)
    {
        var mode = GetHumanListMode(obj);
        var methodName = (mode == "CheckList" || mode == "Sequence") ? "ToggleItem" : "SelectItem";
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

    private static Grasshopper.Kernel.GH_Document? _hostDocument;
    public static Grasshopper.Kernel.GH_Document? HostDocument
    {
        get => _hostDocument;
        set
        {
            if (_hostDocument == value) return;
            if (_hostDocument != null) _hostDocument.SolutionEnd -= OnDocSolutionEnd;
            _hostDocument = value;
            _latestValues.Clear();
            _solveRunning = false;
            if (_hostDocument != null) _hostDocument.SolutionEnd += OnDocSolutionEnd;
        }
    }

    // Static: survives window close/reopen within the same Rhino session
    private static string? _stateSnapshot;
    public static string?  GetSerializedState()  => _stateSnapshot;
    public static string?  PendingFileState      { get; set; }
    public static bool     NeedsFileRestore      { get; set; }

    // ── window geometry persistence ─────────────────────────────────────────
    public static Size?  PendingWindowSize     { get; set; }
    public static Point? PendingWindowLocation { get; set; }

    public static Size?  GetCurrentWindowSize()     => _instance != null && !_instance.IsDisposed ? _instance.Size     : (Size?)null;
    public static Point? GetCurrentWindowLocation()  => _instance != null && !_instance.IsDisposed ? _instance.Location : (Point?)null;

    // ── solve throttle (latest-value batching) ───────────────────────────────
    // Only the most recent value per slider ID survives to the next solve.
    // SetSliderValue is called exactly once per slider per solve, never on every IPC message.
    private static readonly Dictionary<string, double> _latestValues = new();
    private static bool _solveRunning;

    private static void OnDocSolutionEnd(object sender, Grasshopper.Kernel.GH_SolutionEventArgs e)
    {
        // SolutionEnd may fire on GH background thread — marshal to UI thread for thread safety.
        var win = _instance;
        win?.BeginInvoke((Action)(() =>
        {
            if (_latestValues.Count == 0) _solveRunning = false;
            else ApplyLatestValues();
            PushSliderNameUpdates();
            PushPanelTextUpdates();
            PushItemPickerUpdates();
            PushHumanValueListUpdates();
            PushColourPickerUpdates();
        }));
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
            var hex  = ColorToHex(kv.Value.Colour);
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

    private static void ClearPushCaches()
    {
        _lastPushedNames.Clear();
        _lastPushedPanels.Clear();
        _lastPushedHumanLists.Clear();
        _lastPushedColours.Clear();
        _lastPushedPickers.Clear();
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

        foreach (var kv in win._sliders)    PushIfChanged(kv.Key, kv.Value.NickName);
        foreach (var kv in win._toggles)    PushIfChanged(kv.Key, kv.Value.NickName);
        foreach (var kv in win._buttons)    PushIfChanged(kv.Key, kv.Value.NickName);
        foreach (var kv in win._valueLists) PushIfChanged(kv.Key, kv.Value.NickName);
        foreach (var kv in win._pancakeTrueOnlyButtons) PushIfChanged(kv.Key, kv.Value.NickName);
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
            var text     = kv.Value.UserText;
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
            var (options, value, multi) = GetHumanListItems(kv.Value);
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
                multiSelect = multi
            }));
        }
    }

    private static void ApplyLatestValues()
    {
        var win = _instance;
        if (win == null) { _latestValues.Clear(); _solveRunning = false; return; }

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

        _latestValues.Clear();
        _hostDocument?.ScheduleSolution(1, null);
    }

    private static void Log(string msg)
    {
        LogQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }

    // ── singleton ────────────────────────────────────────────────────────────

    public static SlateWindow GetOrCreate()
    {
        if (_instance == null || _instance.IsDisposed)
            _instance = new SlateWindow();
        return _instance;
    }

    public static void EnsureVisible()
    {
        var w = GetOrCreate();

        if (PendingWindowSize is Size size)
        {
            const int minW = 420, minH = 300, maxW = 3000, maxH = 2000;
            w.Size = new Size(
                Math.Max(minW, Math.Min(maxW, size.Width)),
                Math.Max(minH, Math.Min(maxH, size.Height)));
            PendingWindowSize = null;
        }
        if (PendingWindowLocation is Point loc)
        {
            // Only trust a restored position if it still lands on a connected monitor —
            // otherwise fall back to the default corner instead of opening off-screen.
            bool onScreen = Screen.AllScreens.Any(s => s.WorkingArea.Contains(loc.X + 20, loc.Y + 20));
            w.Location = onScreen ? loc : new Point(60, 60);
            PendingWindowLocation = null;
        }

        if (!w.Visible) w.Show();
        w.BringToFront();
    }

    public static void HideIfOpen()
    {
        if (_instance != null && !_instance.IsDisposed && _instance.Visible)
            _instance.Hide();
    }

    // ── construction ─────────────────────────────────────────────────────────

    private SlateWindow()
    {
        Text            = "Slate";
        FormBorderStyle = FormBorderStyle.Sizable;
        Size            = new Size(680, 520);
        MinimumSize     = new Size(420, 300);
        StartPosition   = FormStartPosition.Manual;
        Location        = new Point(60, 60);
        BackColor       = Color.FromArgb(18, 18, 18);

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Load += async (_, _) =>
        {
            try   { await InitWebViewAsync(); }
            catch (Exception ex) { ShowError(ex.Message); }
        };
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
        string userDataFolder = Path.Combine(Path.GetTempPath(), "SlateWebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled            = true;

        _webView.CoreWebView2.WebMessageReceived += OnMessageFromJs;

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "slate.local", GetOrExtractWebRoot(), CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Navigate("https://slate.local/index.html");

        // Re-assert always-on-top after WebView2 init (it can reset window flags)
        SetPin(true);
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
                    if (_colourPickers.TryGetValue(cid, out var picker))
                    {
                        picker.Colour = HexToColor(hex);
                        picker.ExpireSolution(true);
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

                case "ui_ready":
                    // Page (re)loaded — restore state if available
                    if (NeedsFileRestore && PendingFileState is string fs && HostDocument is Grasshopper.Kernel.GH_Document fd)
                    {
                        RestoreState(fs, fd);
                        NeedsFileRestore = false;
                        PendingFileState = null;
                    }
                    else if (!NeedsFileRestore && _stateSnapshot is string ss && HostDocument is Grasshopper.Kernel.GH_Document sd)
                    {
                        RestoreState(ss, sd);
                    }
                    break;

                case "state_snapshot":
                    _stateSnapshot = e.WebMessageAsJson;
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

        var colourPickers = selected.OfType<GH_ColourPickerObject>().ToList();
        foreach (var c in colourPickers)
            AddColourPicker(tabId, groupId, c);

        EnsurePancakeReflection();
        var pancakeButtons = _pancakeTrueOnlyBtnType != null
            ? selected.Where(o => _pancakeTrueOnlyBtnType.IsInstanceOfType(o)).ToList()
            : new List<IGH_DocumentObject>();
        foreach (var pb in pancakeButtons)
            if (pb is IGH_Param pbp) AddPancakeTrueOnlyButton(tabId, groupId, pbp);

        Log($"Captured {sliders.Count} slider(s), {toggles.Count} toggle(s), {buttons.Count} button(s), {valueLists.Count} value list(s), {panels.Count} panel(s), {itemPickers.Count} item picker(s), {humanLists.Count} item selector(s), {colourPickers.Count} colour picker(s), {pancakeButtons.Count} true-only button(s).");
    }

    // ── C# → JS ──────────────────────────────────────────────────────────────

    private void PostToJs(string json)
    {
        if (_webView.CoreWebView2 == null) return;
        _webView.CoreWebView2.PostWebMessageAsString(json);
    }

    // ── public API ────────────────────────────────────────────────────────────

    public void AddSlider(string tabId, GH_NumberSlider slider) =>
        AddSlider(tabId, null, slider);

    public void AddSlider(string tabId, string? groupId, GH_NumberSlider slider)
    {
        string id = slider.InstanceGuid.ToString();
        _sliders[id] = slider;

        PostToJs(SlateEvent.SliderAdded(
            tabId,
            id,
            slider.NickName,
            (double)slider.Slider.Minimum,
            (double)slider.Slider.Maximum,
            (double)slider.CurrentValue,
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

    // CheckList/Sequence allow more than one item checked at once (ToggleItem,
    // value = array of indices); DropDown/Cycle are single-select (SelectItem,
    // value = one index). Cycle isn't distinguished from DropDown yet — same
    // single-select UI for now, just doesn't break.
    private static bool IsMultiSelectMode(GH_ValueListMode mode) =>
        mode == GH_ValueListMode.CheckList || mode == GH_ValueListMode.Sequence;

    private static (object value, bool multi) GetValueListSelection(GH_ValueListMode mode, List<GH_ValueListItem> items)
    {
        bool multi = IsMultiSelectMode(mode);
        if (multi)
        {
            var indices = new List<int>();
            for (int i = 0; i < items.Count; i++) if (items[i].Selected) indices.Add(i);
            return (indices, true);
        }
        return (items.FindIndex(li => li.Selected), false);
    }

    public void AddValueList(string tabId, string? groupId, GH_ValueList valueList)
    {
        string id = valueList.InstanceGuid.ToString();
        _valueLists[id] = valueList;

        var options = valueList.ListItems.Select(i => i.Name);
        var (value, multi) = GetValueListSelection(valueList.ListMode, valueList.ListItems);
        PostToJs(SlateEvent.ValueListAdded(tabId, id, valueList.NickName, options, value, multi, groupId));
    }

    public void AddPanel(string tabId, string? groupId, GH_Panel panel)
    {
        string id = panel.InstanceGuid.ToString();
        _panels[id] = panel;

        PostToJs(SlateEvent.PanelAdded(tabId, id, panel.NickName, panel.UserText, panel.SourceCount > 0, groupId));
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

        var (options, value, multi) = GetHumanListItems(humanValueList);
        PostToJs(SlateEvent.HumanValueListAdded(tabId, id, humanValueList.NickName, options, value, multi, groupId));
    }

    public void AddColourPicker(string tabId, string? groupId, GH_ColourPickerObject picker)
    {
        string id = picker.InstanceGuid.ToString();
        _colourPickers[id] = picker;

        PostToJs(SlateEvent.ColourPickerAdded(tabId, id, picker.NickName, ColorToHex(picker.Colour), groupId));
    }

    public void AddPancakeTrueOnlyButton(string tabId, string? groupId, IGH_Param button)
    {
        string id = button.InstanceGuid.ToString();
        _pancakeTrueOnlyButtons[id] = button;

        PostToJs(SlateEvent.PancakeButtonAdded(tabId, id, button.NickName, GetPancakeButtonDown(button), groupId));
    }

    public void ClearAll()
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
        ClearPushCaches();
        PostToJs(SlateEvent.Cleared());
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
                .OfType<GH_ColourPickerObject>()
                .ToDictionary(c => c.InstanceGuid.ToString(), c => c);
            EnsurePancakeReflection();
            var docPancakeButtons = (_pancakeTrueOnlyBtnType != null
                    ? doc.Objects.Where(o => _pancakeTrueOnlyBtnType.IsInstanceOfType(o)).OfType<IGH_Param>()
                    : Enumerable.Empty<IGH_Param>())
                .ToDictionary(p => p.InstanceGuid.ToString(), p => p);

            _sliders.Clear();
            _toggles.Clear();
            _buttons.Clear();
            _valueLists.Clear();
            _panels.Clear();
            _itemPickers.Clear();
            _humanValueLists.Clear();
            _colourPickers.Clear();
            _pancakeTrueOnlyButtons.Clear();

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
                        else arr.RemoveAt(i);
                    }
                    else if (kind == "button")
                    {
                        if (docButtons.TryGetValue(id, out var btn))
                        {
                            _buttons[id] = btn;
                            s["value"] = btn.ButtonDown ? 1 : 0;
                            s["name"]  = btn.NickName;
                        }
                        else arr.RemoveAt(i);
                    }
                    else if (kind == "valueList")
                    {
                        if (docValueLists.TryGetValue(id, out var vl))
                        {
                            _valueLists[id] = vl;
                            var (value, multi) = GetValueListSelection(vl.ListMode, vl.ListItems);
                            s["value"] = multi
                                ? new JsonArray(((List<int>)value).Select(v => JsonValue.Create(v)).ToArray())
                                : JsonValue.Create((int)value);
                            s["name"]        = vl.NickName;
                            s["multiSelect"] = multi;
                            s["options"]     = new JsonArray(vl.ListItems.Select(li => JsonValue.Create(li.Name)).ToArray());
                        }
                        else arr.RemoveAt(i);
                    }
                    else if (kind == "panel")
                    {
                        if (docPanels.TryGetValue(id, out var pnl))
                        {
                            _panels[id] = pnl;
                            s["value"]    = pnl.UserText;
                            s["name"]     = pnl.NickName;
                            s["readOnly"] = pnl.SourceCount > 0;
                        }
                        else arr.RemoveAt(i);
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
                        else arr.RemoveAt(i);
                    }
                    else if (kind == "humanValueList")
                    {
                        if (docHumanValueLists.TryGetValue(id, out var hvl))
                        {
                            _humanValueLists[id] = hvl;
                            var (options, value, multi) = GetHumanListItems(hvl);
                            s["value"] = multi
                                ? new JsonArray(((List<int>)value).Select(v => JsonValue.Create(v)).ToArray())
                                : JsonValue.Create((int)value);
                            s["name"]        = hvl.NickName;
                            s["multiSelect"] = multi;
                            s["options"]     = new JsonArray(options.Select(o => JsonValue.Create(o)).ToArray());
                        }
                        else arr.RemoveAt(i);
                    }
                    else if (kind == "colourPicker")
                    {
                        if (docColourPickers.TryGetValue(id, out var cp))
                        {
                            _colourPickers[id] = cp;
                            s["value"] = ColorToHex(cp.Colour);
                            s["name"]  = cp.NickName;
                        }
                        else arr.RemoveAt(i);
                    }
                    else if (kind == "pancakeButton")
                    {
                        if (docPancakeButtons.TryGetValue(id, out var pbtn))
                        {
                            _pancakeTrueOnlyButtons[id] = pbtn;
                            s["value"] = GetPancakeButtonDown(pbtn) ? 1 : 0;
                            s["name"]  = pbtn.NickName;
                        }
                        else arr.RemoveAt(i);
                    }
                    else if (docSliders.TryGetValue(id, out var gh))
                    {
                        _sliders[id] = gh;
                        s["value"]   = (double)gh.CurrentValue;
                        s["min"]     = (double)gh.Slider.Minimum;
                        s["max"]     = (double)gh.Slider.Maximum;
                        s["name"]    = gh.NickName;
                    }
                    else arr.RemoveAt(i);
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

            state["type"] = "restore_state";
            PostToJs(state.ToJsonString());
            Log($"State restored: {_sliders.Count} slider(s), {_toggles.Count} toggle(s), {_buttons.Count} button(s), {_valueLists.Count} value list(s), {_panels.Count} panel(s), {_itemPickers.Count} item picker(s), {_humanValueLists.Count} item selector(s), {_colourPickers.Count} colour picker(s), {_pancakeTrueOnlyButtons.Count} true-only button(s).");
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
        string rel = resourceName["Slate.Resources.".Length..];

        // Known web extensions — find the last occurrence to reconstruct the path
        string[] exts = { ".html", ".js", ".css", ".svg", ".png", ".ico", ".json" };
        foreach (var ext in exts)
        {
            string extKey = ext.TrimStart('.');
            int idx = rel.LastIndexOf('.' + extKey);
            if (idx < 0) continue;

            string withoutExt = rel[..idx].Replace('.', Path.DirectorySeparatorChar);
            return withoutExt + ext;
        }

        return rel.Replace('.', Path.DirectorySeparatorChar);
    }
}
