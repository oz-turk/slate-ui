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

    public static string HumanValueListAdded(string tabId, string id, string name, IEnumerable<string> options, object value, bool multiSelect, bool cycle, bool loop, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "humanValueList_added", tabId, id, name, options, value, multiSelect, cycle, loop, groupId });

    // Native geometry-holding param (Point/Curve/Brep/Mesh/Surface/SubD/Box/
    // generic Geometry) — geomKind is the wire "kind" string SlateWindow's
    // TryGetGeometryParamKind maps concrete Param_* classes to. "internalize"
    // is deliberately absent here — it's a pure UI preference with no live GH
    // counterpart, so JS defaults it itself and owns it from then on.
    public static string GeometryParamAdded(string tabId, string id, string name, string geomKind, int count, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "geometryParam_added", tabId, id, name, geomKind, count, groupId });

    // Sent after a "geometry_pick"/"geometry_internalize_change" round trip,
    // and on every solve for a captured param (catches deletion/rename made
    // directly on the canvas, same reasoning as PushTriggerUpdates). geomKind
    // can't change from any of these; internalize is JS-owned and never
    // echoed back.
    public static string GeometryParamUpdated(string id, string name, int count) =>
        JsonSerializer.Serialize(new { type = "geometryParam_updated", id, name, count });

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
