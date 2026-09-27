using System.Text.Json;

namespace Slate.Bridge;

public static class SlateEvent
{
    public static string SliderAdded(string tabId, string id, string name, double min, double max, double value, int decimalPlaces, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "slider_added", tabId, id, name, min, max, value, decimalPlaces, groupId });

    public static string ToggleAdded(string tabId, string id, string name, bool value, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "toggle_added", tabId, id, name, value = value ? 1 : 0, groupId });

    public static string ButtonAdded(string tabId, string id, string name, bool value, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "button_added", tabId, id, name, value = value ? 1 : 0, groupId });

    // Pancake plugin's "True Only Button" — same ButtonDown shape as core GH_ButtonObject,
    // kept as its own wire type so it round-trips through RestoreState correctly.
    public static string PancakeButtonAdded(string tabId, string id, string name, bool value, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "pancakeButton_added", tabId, id, name, value = value ? 1 : 0, groupId });

    public static string ValueListAdded(string tabId, string id, string name, IEnumerable<string> options, object value, bool multiSelect, bool cycle, bool loop, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "valueList_added", tabId, id, name, options, value, multiSelect, cycle, loop, groupId });

    public static string PanelAdded(string tabId, string id, string name, string text, bool readOnly, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "panel_added", tabId, id, name, value = text, readOnly, groupId });

    public static string ItemPickerAdded(string tabId, string id, string name, IEnumerable<string> options, int selectedIndex, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "itemPicker_added", tabId, id, name, options, value = selectedIndex, groupId });

    public static string ColourPickerAdded(string tabId, string id, string name, string hex, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "colourPicker_added", tabId, id, name, value = hex, groupId });

    // GH_Timer ("Trigger") — interval's sign IS the mode (negative = Manual,
    // positive = cyclic, value = ms); intervalString is GH's own formatted
    // display text ("1 second", "----------" for Manual), sent so Slate never
    // has to reimplement that formatting for the initial/native-driven state.
    public static string TriggerAdded(string tabId, string id, string name, int interval, string intervalString, bool lockTargets, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "trigger_added", tabId, id, name, interval, intervalString, lockTargets, groupId });

    // Native "Data Dam" (Grasshopper.Kernel.Components.GH_DataDamComponent) —
    // unlike GH_Timer this is a genuine data-carrying GH_Component, but it's
    // captured the same "not an IGH_Param" way. mode is "always"/"never"/"delay"
    // (mirrors the native right-click Mode/Delay submenu, which is one preset
    // list, not two separate controls — see yapilacaklar/data-dam-capture.md);
    // delayLabel is our own formatted text (no native IntervalString-style
    // property exists here) so JS never reimplements "X seconds" formatting.
    // gateLinked/gateName describe the optional Select Gate link — always
    // false/null on a fresh capture. transferPossible mirrors the native
    // property of the same name (true = there's new input data the dam
    // hasn't sent out yet) — JS uses it to swap the release icon and disable
    // the button the same way native dims its own click target and changes
    // its tooltip when there's nothing to do (see datadam_fire's own
    // TransferPossible check in SlateWindow.cs for the matching backend half).
    public static string DataDamAdded(string tabId, string id, string name, string mode, double delaySeconds, string delayLabel, bool transferPossible, bool gateLinked, string? gateName, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "dataDam_added", tabId, id, name, mode, delaySeconds, delayLabel, transferPossible, gateLinked, gateName, groupId });

    // Periodic push (OnDocSolutionEnd, same PushTriggerUpdates reasoning) —
    // catches Mode/Delay/name changes made directly on the native canvas, and
    // a gate source's own rename or deletion (gateLinked flips false once its
    // source is gone — see PushDataDamGateUpdates). Never carries gateId: the
    // link itself only ever changes via an explicit Select Gate action
    // (DataDamGateSelected), not from anything a solve alone could cause.
    public static string DataDamUpdated(string id, string name, string mode, double delaySeconds, string delayLabel, bool transferPossible, bool gateLinked, string? gateName) =>
        JsonSerializer.Serialize(new { type = "dataDam_update", id, name, mode, delaySeconds, delayLabel, transferPossible, gateLinked, gateName });

    // One-shot reply to "datadam_select_gate" — the only message that carries
    // gateId, because that id is pure Slate bookkeeping with no live GH
    // counterpart (like GeometryParamAdded's "internalize"): RestoreState can't
    // refresh it from anywhere, so JS persists it explicitly (see App.svelte's
    // handler calling postStateSnapshot right after this one, unlike
    // dataDam_update above which never does).
    public static string DataDamGateSelected(string id, bool gateLinked, string? gateName, string? gateId) =>
        JsonSerializer.Serialize(new { type = "dataDam_gate_selected", id, gateLinked, gateName, gateId });

    public static string HumanValueListAdded(string tabId, string id, string name, IEnumerable<string> options, object value, bool multiSelect, bool cycle, bool loop, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "humanValueList_added", tabId, id, name, options, value, multiSelect, cycle, loop, groupId });

    // Native geometry-holding param (Point/Curve/Brep/Mesh/Surface/SubD/Box/
    // generic Geometry) — geomKind is the wire "kind" string SlateWindow's
    // TryGetGeometryParamKind maps concrete Param_* classes to. "internalize"
    // is deliberately absent here — it's a pure UI preference with no live GH
    // counterpart, so JS defaults it itself and owns it from then on. "wired"
    // (SourceCount > 0) tells JS to disable the Pick button — the param's data
    // comes from its own upstream source, so a pick would just be overwritten
    // on the next solve; Internalize disconnects the source and clears it.
    public static string GeometryParamAdded(string tabId, string id, string name, string geomKind, int count, bool wired, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "geometryParam_added", tabId, id, name, geomKind, count, wired, groupId });

    // Sent after a "geometry_pick"/"geometry_internalize_change" round trip,
    // and on every solve for a captured param (catches deletion/rename made
    // directly on the canvas, same reasoning as PushTriggerUpdates). geomKind
    // can't change from any of these; internalize is JS-owned and never
    // echoed back. wired — see GeometryParamAdded — flips back to false once
    // an Internalize disconnects the param's source, re-enabling Pick.
    public static string GeometryParamUpdated(string id, string name, int count, bool wired) =>
        JsonSerializer.Serialize(new { type = "geometryParam_updated", id, name, count, wired });

    // Native "Param Viewer" (Grasshopper.Kernel.Special.GH_ParamViewer). `tree`
    // is a generic { kids: [...] } (grouping level) / { count } (real branch)
    // shape built from GH's own GraphicTree (see BuildParamViewerTree in
    // SlateWindow.cs) — not GH-specific, so the front end's sunburst/layout
    // code isn't tied to this one capture path. See yapilacaklar/data-tree-editor.md.
    public static string ParamViewerAdded(string tabId, string id, string name, object tree, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "paramViewer_added", tabId, id, name, tree, groupId });

    // Periodic push (OnDocSolutionEnd) — a Param Viewer's tree changes
    // whenever its wired source's data changes, same "recompute every solve"
    // reasoning as PanelTextUpdated.
    public static string ParamViewerUpdated(string id, string name, object tree) =>
        JsonSerializer.Serialize(new { type = "paramViewer_update", id, name, tree });

    // Reply to "paramViewer_request_items" (Data Tree Explorer's leaf item
    // view, drilling all the way into a real branch's actual values, not just
    // its count) — fetched fresh per request, never cached/stored on the C#
    // side. `path` is echoed back unchanged so JS can key its own item cache
    // without needing to serialize the path itself.
    public static string ParamViewerItemsResult(string id, IEnumerable<int> path, IEnumerable<string> items) =>
        JsonSerializer.Serialize(new { type = "paramViewer_items_result", id, path, items });

    // Reply to Pane's "sort_positions_request" (right-click Sort: Canvas
    // Position) — live GH pivot per requested id, as [x, y]. Computed fresh
    // for this one round trip; never stored on the C# or JS side.
    public static string SortPositionsResult(string tabId, IDictionary<string, float[]> positions) =>
        JsonSerializer.Serialize(new { type = "sort_positions_result", tabId, positions });

    public static string ZoomChanged(double factor) =>
        JsonSerializer.Serialize(new { type = "zoom_changed", factor });

    // Ground-truth Alt state, polled via GetAsyncKeyState — see the poll
    // timer in SlateWindow.cs for why the DOM's own keydown/keyup can't be
    // trusted for this on its own.
    public static string AltStateChanged(bool held) =>
        JsonSerializer.Serialize(new { type = "alt_state", held });

    // Ground-truth 'c'/'x' hotkey trigger, polled via GetAsyncKeyState — same
    // reasoning as AltStateChanged, see the poll timer in SlateWindow.cs.
    public static string CaptureHotkey() =>
        JsonSerializer.Serialize(new { type = "capture_hotkey" });

    public static string DeleteHotkey() =>
        JsonSerializer.Serialize(new { type = "delete_hotkey" });

    public static string GroupHotkey() =>
        JsonSerializer.Serialize(new { type = "group_hotkey" });

    public static string Cleared() =>
        JsonSerializer.Serialize(new { type = "cleared" });

    // Unlike Cleared (empties sliders/groups but keeps tabs/panes/workspaces),
    // Reset drops the whole layout back to one empty workspace/pane/tab.
    public static string Reset() =>
        JsonSerializer.Serialize(new { type = "reset" });
}
