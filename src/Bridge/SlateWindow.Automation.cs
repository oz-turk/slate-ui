using Grasshopper.Kernel;
using System.Text.Json;

namespace Slate.Bridge;

public partial class SlateWindow
{
    // ── scripting entry point ────────────────────────────────────────────────
    // A small, synchronous surface for driving Slate from a script running
    // inside Rhino (RhinoCode / C# script / remote scripting tools), so a
    // script doesn't have to reach into private fields or the page's DOM.
    //
    // EXPERIMENTAL: names and signatures may change between versions.
    //
    // Deliberately narrow: capture, value writes, scenes and a state read.
    // Anything with side effects on the Rhino/GH document beyond control
    // values (bake, clear, internalize, Path Mapper apply, geometry pick) is
    // left out on purpose. Every call goes through the same paths the UI
    // uses, so undo, the "modified" dot and state saving behave as usual.
    //
    // Call on Rhino's UI thread. Every method returns a short status string
    // ("ok: ..." / "error: ...") instead of throwing.
    public static class Automation
    {
        static SlateWindow? Window =>
            _instance != null && !_instance.IsDisposed && _instance._webView?.CoreWebView2 != null ? _instance : null;

        const string NoWindow = "error: Slate window is not open";

        /// <summary>Captures the given GH objects (by InstanceGuid) into a tab,
        /// optionally inside a Slate group. Same rules as the Capture button:
        /// canvas order, GH groups become Slate groups.</summary>
        public static string Capture(IEnumerable<Guid> ids, string tabId = "main", string? groupId = null)
        {
            var win = Window;
            if (win == null) return NoWindow;
            var doc = HostDocument ?? Grasshopper.Instances.ActiveCanvas?.Document;
            if (doc == null) return "error: no Grasshopper document";

            var objs = new List<IGH_DocumentObject>();
            var missing = new List<Guid>();
            foreach (var id in ids)
            {
                var o = doc.FindObject(id, true);
                if (o != null) objs.Add(o); else missing.Add(id);
            }

            EnsureHumanReflection();
            EnsurePancakeReflection();
            var counts = new Dictionary<string, int>();
            win.CaptureObjects(doc, tabId, groupId, objs, counts, new HashSet<Guid>());

            string got = counts.Count == 0
                ? "nothing capturable"
                : string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key}"));
            return missing.Count == 0
                ? $"ok: captured {got}"
                : $"ok: captured {got}; not found: {string.Join(", ", missing)}";
        }

        /// <summary>Sets a numeric control (slider, toggle 0/1, value list,
        /// item picker) to a value. Batched into the next solve like a drag.
        /// The UI picks the new value up when that solve ends.</summary>
        public static string SetValue(string id, double value)
        {
            var win = Window;
            if (win == null) return NoWindow;
            if (!(win._sliders.ContainsKey(id) || win._toggles.ContainsKey(id) || win._valueLists.ContainsKey(id)
                  || win._itemPickers.ContainsKey(id) || win._humanValueLists.ContainsKey(id)))
                return $"error: no captured numeric control with id {id}";
            _latestValues[id] = value;
            if (!_solveRunning) { _solveRunning = true; ApplyLatestValues(); }
            return "ok";
        }

        /// <summary>Sets a captured colour picker to a CSS hex colour (#rrggbb or #rrggbbaa).</summary>
        public static string SetColour(string id, string hex)
        {
            var win = Window;
            if (win == null) return NoWindow;
            if (!win._colourPickers.ContainsKey(id)) return $"error: no captured colour picker with id {id}";
            _latestColourValues[id] = hex;
            if (!_solveRunning) { _solveRunning = true; ApplyLatestValues(); }
            return "ok";
        }

        /// <summary>Saves the current values as a new scene. Asynchronous on
        /// the page side: the scene shows up in GetState() once the page has
        /// sent its next state snapshot. Values set with SetValue are only
        /// recorded after their solve has finished.</summary>
        public static string SaveScene(string name) => SceneAction("save", name);

        /// <summary>Overwrites the scene with this name (or id) with the current values.</summary>
        public static string OverwriteScene(string nameOrId) => SceneAction("overwrite", nameOrId);

        /// <summary>Applies the scene with this name (or id).</summary>
        public static string ApplyScene(string nameOrId) => SceneAction("apply", nameOrId);

        /// <summary>Deletes the scene with this name (or id).</summary>
        public static string DeleteScene(string nameOrId) => SceneAction("delete", nameOrId);

        static string SceneAction(string action, string target)
        {
            var win = Window;
            if (win == null) return NoWindow;
            if (string.IsNullOrWhiteSpace(target)) return "error: empty scene name";
            win.PostToJs(JsonSerializer.Serialize(new { type = "automation_scene", action, target }));
            return "ok: sent";
        }

        /// <summary>The last UI state the page saved for the current document
        /// (workspaces, panes, tabs, captured controls with their ids and
        /// values, scenes), as JSON. Null if nothing has been saved yet.</summary>
        public static string? GetState() =>
            HostDocument != null && _uiStateByDoc.TryGetValue(HostDocument, out var s) ? s : null;
    }
}
