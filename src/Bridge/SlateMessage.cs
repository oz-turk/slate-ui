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

    public static string HumanValueListAdded(string tabId, string id, string name, IEnumerable<string> options, object value, bool multiSelect, bool cycle, bool loop, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "humanValueList_added", tabId, id, name, options, value, multiSelect, cycle, loop, groupId });

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

    // epoch: current SlateWindow._syncEpoch, echoed back by JS on every
    // subsequent state_snapshot (see ipc.js's postStateSnapshot) so a stale
    // snapshot — queued or throttled from before this switch, e.g. a
    // resize-debounce timer that survived a hide/show cycle — can be told
    // apart from a fresh one and dropped instead of overwriting the right
    // document's cache with the wrong one's layout. See SyncToDocument.
    public static string Cleared(int epoch) =>
        JsonSerializer.Serialize(new { type = "cleared", epoch });

    // Unlike Cleared (empties sliders/groups but keeps tabs/panes/workspaces),
    // Reset drops the whole layout back to one empty workspace/pane/tab.
    public static string Reset(int epoch) =>
        JsonSerializer.Serialize(new { type = "reset", epoch });
}
