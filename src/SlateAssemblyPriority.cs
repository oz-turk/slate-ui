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

        // The canvas doesn't exist yet at plugin load (it's created when the
        // Grasshopper editor window first opens), so hook DocumentChanged via
        // CanvasCreated rather than Instances.ActiveCanvas directly. Lets
        // SlateWindow re-sync which document's UI is showing whenever the user
        // switches between already-open .gh tabs, not just when a Slate panel
        // is placed/removed — see SlateWindow.OnActiveDocumentChanged.
        Grasshopper.Instances.CanvasCreated += canvas =>
            canvas.DocumentChanged += (_, e) => SlateWindow.OnActiveDocumentChanged(e.NewDocument);

        // Per-document ui_state/geometry caches (see SlateWindow's per-doc
        // dictionaries) are keyed by GH_Document and otherwise never evicted,
        // so a file closed for good — as opposed to a Slate component merely
        // deleted from a still-open file — needs to drop its entry here or it
        // leaks for the rest of the Rhino session.
        Grasshopper.Instances.DocumentServer.DocumentRemoved += (_, doc) =>
            SlateWindow.EvictDocument(doc);

        return GH_LoadingInstruction.Proceed;
    }
}
