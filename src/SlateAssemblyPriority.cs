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
        Grasshopper.Instances.ComponentServer.AddCategoryIcon("Slate",
            SlateLogo.ToBitmap(24, SlateLogo.CanvasOutline, SlateLogo.CanvasFill));
        Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Slate", 'S');
        return GH_LoadingInstruction.Proceed;
    }
}
