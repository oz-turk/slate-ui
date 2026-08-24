using System.Text.Json;

namespace Slate.Bridge;

public static class SlateEvent
{
    public static string SliderAdded(string tabId, string id, string name, double min, double max, double value, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "slider_added", tabId, id, name, min, max, value, groupId });

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

    public static string Cleared() =>
        JsonSerializer.Serialize(new { type = "cleared" });

    // Unlike Cleared (empties sliders/groups but keeps tabs/panes/workspaces),
    // Reset drops the whole layout back to one empty workspace/pane/tab.
    public static string Reset() =>
        JsonSerializer.Serialize(new { type = "reset" });
}
