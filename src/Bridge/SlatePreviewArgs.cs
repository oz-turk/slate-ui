using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using System.Drawing;

namespace Slate.Bridge;

// Minimal IGH_PreviewArgs implementation used by PreviewPinConduit to draw
// "pinned" objects outside GH's own PreviewFilter/PreviewMode/PreviewBoundary
// pipeline (see PreviewPinConduit for why: none of those expose a per-object
// override, so pinned objects get their own independent draw call instead).
// Colours/mesh params are borrowed straight from the live GH_Document so a
// pinned object renders identically to GH's own default (non-selected)
// preview — WireColour_Selected/ShadeMaterial_Selected are only populated
// because the interface requires them; pinned objects are never drawn as
// "selected" on purpose (see conversation: avoids touching real GH selection
// state and its side effects on canvas interaction).
internal sealed class SlatePreviewArgs : IGH_PreviewArgs
{
    public RhinoViewport Viewport { get; }
    public DisplayPipeline Display { get; }
    public GH_Document Document { get; }
    public Color WireColour { get; }
    public Color WireColour_Selected { get; }
    public DisplayMaterial ShadeMaterial { get; }
    public DisplayMaterial ShadeMaterial_Selected { get; }
    public MeshingParameters MeshingParameters { get; }
    public int DefaultCurveThickness { get; }

    public SlatePreviewArgs(DrawEventArgs e, GH_Document doc)
    {
        Viewport = e.Viewport;
        Display = e.Display;
        Document = doc;
        // GH_Document.PreviewDrawObjects (decompiled 2026-09-20, twice — once
        // to chase a shading mismatch, again to chase a curve/point one)
        // strips alpha off the wire colours before using them (opaque wires
        // even though PreviewColour/PreviewColourSelected carry GH's own
        // ~59%-alpha default) — passing the un-stripped colour straight
        // through made our wires render semi-transparent/faded next to GH's
        // own. Meshes are unaffected: CreateStandardMaterial below gets the
        // full alpha-including colour in GH's own code too.
        WireColour = Color.FromArgb(doc.PreviewColour.R, doc.PreviewColour.G, doc.PreviewColour.B);
        WireColour_Selected = Color.FromArgb(doc.PreviewColourSelected.R, doc.PreviewColourSelected.G, doc.PreviewColourSelected.B);
        // Same decompile: GH doesn't invent its own thickness constant, it
        // just forwards the viewport's own DisplayPipeline.DefaultCurveThickness —
        // the hardcoded 3 this used to have could differ from a given
        // viewport's actual default and drew visibly thinner/thicker curves.
        DefaultCurveThickness = e.Display.DefaultCurveThickness;
        ShadeMaterial = GH_Material.CreateStandardMaterial(doc.PreviewColour);
        ShadeMaterial_Selected = GH_Material.CreateStandardMaterial(doc.PreviewColourSelected);
        MeshingParameters = doc.PreviewCustomMeshParameters ?? MeshingParameters.Default;
    }
}
