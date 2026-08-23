using Grasshopper.Kernel;
using Slate.Bridge;

namespace Slate;

// Runs once before component discovery. Without this, the "Slate" ribbon
// tab falls back to GH's default single-letter tab icon instead of the
// brand mark.
public class SlateAssemblyPriority : GH_AssemblyPriority
{
    public override GH_LoadingInstruction PriorityLoad()
    {
        // Ribbon tab icon renders clipped-looking at full size in the toolbar's
        // fixed slot — shrunk 10% (scale param, doesn't affect SlatePanel.Icon).
        Grasshopper.Instances.ComponentServer.AddCategoryIcon("Slate",
            SlateLogo.ToBitmap(24, SlateLogo.CanvasOutline, SlateLogo.CanvasFill, scale: 0.9f));
        Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Slate", 'S');
        return GH_LoadingInstruction.Proceed;
    }
}
