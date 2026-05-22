using System.Text.Json;

namespace Slate.Bridge;

public static class SlateEvent
{
    public static string SliderAdded(string tabId, string id, string name, double min, double max, double value) =>
        JsonSerializer.Serialize(new { type = "slider_added", tabId, id, name, min, max, value });

    public static string Cleared() =>
        JsonSerializer.Serialize(new { type = "cleared" });
}
