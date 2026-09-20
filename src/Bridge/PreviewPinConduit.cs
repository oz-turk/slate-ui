using Grasshopper.Kernel;
using Rhino.Display;

namespace Slate.Bridge;

// Draws Slate's "pinned" groups' preview geometry regardless of GH's own
// PreviewFilter (the "only draw preview for selected objects" canvas mode),
// PreviewMode (on/wireframe/shaded/off), or PreviewBoundary — none of these
// expose a per-object override, and toggling the real GH selection state to
// fake it would have side effects on canvas interaction (right-click menus,
// group/delete commands, etc. — see conversation). So pinned objects get
// their own draw pass here instead, using the same colours/mesh params GH's
// own default (non-selected) preview uses (see SlatePreviewArgs).
internal sealed class PreviewPinConduit : DisplayConduit
{
    private static readonly PreviewPinConduit _instance = new();

    // Keyed by document, not a single flat list — SlateWindow already juggles
    // multiple open .gh files (see multi-window-state-sync), and this conduit
    // is a single global Rhino object shared across all of them.
    private static readonly Dictionary<GH_Document, List<IGH_PreviewObject>> _pinsByDoc = new();

    private PreviewPinConduit()
    {
        Enabled = true;
    }

    public static void SetPins(GH_Document doc, List<IGH_PreviewObject> objects)
    {
        _ = _instance; // touch the static field so the conduit is constructed/registered even if this is the first call
        if (objects.Count == 0) _pinsByDoc.Remove(doc);
        else _pinsByDoc[doc] = objects;
    }

    public static void ClearPins(GH_Document doc) => _pinsByDoc.Remove(doc);

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        // Only draw for whichever document GH's own canvas currently shows —
        // matches GH's own preview behaviour (a background/non-active .gh
        // document never draws preview either) and avoids drawing pins that
        // belong to a document not associated with this viewport.
        var canvasDoc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (canvasDoc == null || !_pinsByDoc.TryGetValue(canvasDoc, out var pins) || pins.Count == 0) return;

        var args = new SlatePreviewArgs(e, canvasDoc);
        foreach (var obj in pins)
        {
            if (!obj.IsPreviewCapable) continue;
            obj.DrawViewportMeshes(args);
            obj.DrawViewportWires(args);
        }
    }
}
