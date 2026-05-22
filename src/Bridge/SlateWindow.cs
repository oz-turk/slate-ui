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

    private readonly WebView2 _webView = new();
    private readonly Dictionary<string, GH_NumberSlider> _sliders = new();

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
        }));
    }

    private static void PushSliderNameUpdates()
    {
        var sliders = _instance?._sliders;
        if (sliders == null) return;
        foreach (var kv in sliders)
        {
            var name = kv.Value.NickName;
            if (string.IsNullOrWhiteSpace(name)) continue;
            _instance?.PostToJs(System.Text.Json.JsonSerializer.Serialize(new {
                type = "slider_name_update",
                id   = kv.Key,
                name
            }));
        }
    }

    private static void ApplyLatestValues()
    {
        var sliders = _instance?._sliders;
        if (sliders == null) { _latestValues.Clear(); _solveRunning = false; return; }

        foreach (var kv in _latestValues)
            if (sliders.TryGetValue(kv.Key, out var s))
            {
                s.SetSliderValue((decimal)kv.Value);
                s.ExpireSolution(false);   // expire without scheduling
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
                    if (_sliders.ContainsKey(sid))
                    {
                        _latestValues[sid] = value;   // always overwrite — only last value survives
                        if (!_solveRunning) { _solveRunning = true; ApplyLatestValues(); }
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
        Log($"CaptureSelected called, tabId='{tabId}', groupId='{groupId}'");

        var doc = HostDocument
               ?? Grasshopper.Instances.ActiveCanvas?.Document;
        Log($"Document: {(doc == null ? "NULL" : $"ok, {doc.ObjectCount} objects")}");

        if (doc == null) return;

        var allSelected = doc.Objects.Where(o => o.Attributes?.Selected == true).ToList();
        Log($"Selected objects: {allSelected.Count}");

        var sliders = allSelected.OfType<GH_NumberSlider>().ToList();
        Log($"Selected sliders: {sliders.Count}");

        foreach (var s in sliders)
        {
            Log($"  Adding: '{s.NickName}' [{s.Slider.Minimum}–{s.Slider.Maximum}]");
            AddSlider(tabId, groupId, s);
        }

        Log($"Capture done.");
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

    public void ClearAll()
    {
        _sliders.Clear();
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

            _sliders.Clear();

            void ProcessSliders(JsonArray arr)
            {
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    var s  = arr[i]!.AsObject();
                    var id = s["id"]!.GetValue<string>();
                    if (docSliders.TryGetValue(id, out var gh))
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
                ProcessSliders(tab["sliders"]!.AsArray());
                foreach (var g in tab["groups"]!.AsArray())
                    ProcessSliders(g!.AsObject()["sliders"]!.AsArray());
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

            // New format: state has "layout" key; legacy: state has "tabs" key
            if (state["layout"] is JsonObject layoutNode)
                ProcessNode(layoutNode);
            else
                ProcessNode(state);

            state["type"] = "restore_state";
            PostToJs(state.ToJsonString());
            Log($"State restored: {_sliders.Count} slider(s).");
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
