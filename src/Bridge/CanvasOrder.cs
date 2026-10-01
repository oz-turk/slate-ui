using Grasshopper.Kernel;

namespace Slate.Bridge;

// Canvas reading order for Capture: columns left to right, top to bottom
// within each column. Same rule as the UI's "Sort: Canvas Position"
// (rankByColumns in ui/src/App.svelte) — keep the two in step.
//
// GH definitions read left to right with inputs stacked in vertical columns.
// Columns are decided by horizontal OVERLAP of Bounds, not by comparing left
// edges: a narrow toggle right-aligned under a wide slider has a much larger
// left X than the slider but sits squarely under it, and overlap handles
// left/right/centre alignment alike. Items are visited left to right; one
// joins the current column if its extent overlaps the column's running extent
// by at least half of the narrower of the two, otherwise it starts a new one.
internal static class CanvasOrder
{
    public static List<T> Sort<T>(IEnumerable<T> objects) where T : IGH_DocumentObject
    {
        var items = objects.ToList();
        if (items.Count < 2) return items;

        var col = new Dictionary<T, int>();
        float colL = 0, colR = 0;
        int current = -1;
        foreach (var o in items.OrderBy(o => o.Attributes.Bounds.Left))
        {
            float l = o.Attributes.Bounds.Left, r = o.Attributes.Bounds.Right;
            float overlap = Math.Min(r, colR) - Math.Max(l, colL);
            if (current < 0 || overlap < 0.5f * Math.Min(r - l, colR - colL))
            {
                current++; colL = l; colR = r;
            }
            else
            {
                colL = Math.Min(colL, l); colR = Math.Max(colR, r);
            }
            col[o] = current;
        }

        return items
            .OrderBy(o => col[o])
            .ThenBy(o => o.Attributes.Pivot.Y)
            .ThenBy(o => o.Attributes.Bounds.Left)
            .ToList();
    }
}
