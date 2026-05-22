using System.Text.Json;

namespace Slate.Bridge;

public static class SlateEvent
{
    public static string SliderAdded(string tabId, string id, string name, double min, double max, double value, string? groupId = null) =>
        JsonSerializer.Serialize(new { type = "slider_added", tabId, id, name, min, max, value, groupId });

    public static string Cleared() =>
        JsonSerializer.Serialize(new { type = "cleared" });
}
