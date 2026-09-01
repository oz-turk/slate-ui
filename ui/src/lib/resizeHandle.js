// Shared custom-resize-handle math for the two row types with a resizable
// body (PanelRow, ValueListRow's checklist mode) — a native CSS `resize`
// wasn't used because the drag needs to snap to MODULE increments (Ctrl) and
// clamp to the pane's visible area. The two components keep their own local
// `resizing`/`liveHeight`/etc state (Svelte only tracks reactive-variable
// assignments made directly in the owning component), and call into these
// pure functions for the actual math instead of each re-deriving it.

export function resizeHint(h, snapped) {
  return `${h}px${snapped ? ' (snapped)' : ''}  ·  Hold Ctrl to snap to slider-row size`
}

// header/margin/padding/border around the resizable body is not itself a
// multiple of MODULE, so snapping the body alone can never land the row's
// outer edge on the grid — measured live (not hardcoded) so it keeps working
// if the surrounding layout ever changes. The resize handle itself is
// absolutely positioned into the row's existing bottom padding (see
// PanelRow/ValueListRow .resize-handle CSS) so it never adds to rowEl's own
// layout height — offsetHeight is already the same in edit and preview,
// nothing to compensate for here.
export function measureResizeBounds(rowEl, startHeight) {
  const resizeOverhead = rowEl ? rowEl.offsetHeight - startHeight : 0
  const contentEl = rowEl?.closest('.content')
  const maxBodyHeight = contentEl && rowEl
    ? contentEl.getBoundingClientRect().bottom - rowEl.getBoundingClientRect().top - resizeOverhead
    : Infinity
  return { resizeOverhead, maxBodyHeight }
}

// New body height for the pointer's travel since resize-start, optionally
// snapped to whole MODULE increments, clamped to [minBodyHeight, maxBodyHeight].
//
// minBodyHeight is "whatever body makes the ROW's total (body + overhead)
// land on the nearest MODULE at or above it" — same accounting PanelRow's
// fitHeight uses for its own auto-fit floor — rather than a flat MODULE. A
// flat 44px floor on the body alone leaves the row's real total at
// `44 + overhead`, which is bigger than one MODULE (and off-grid) as soon as
// there's any overhead at all — so a header-less panel could never shrink
// down to one slider's height, and even the Ctrl-snap above, which DOES
// account for overhead, would have its correctly-snapped smallest value
// overwritten by this floor right after.
export function computeResizeHeight({ clientY, resizeStartY, resizeStartH, resizeOverhead, maxBodyHeight, snapped, MODULE }) {
  let h = resizeStartH + (clientY - resizeStartY)
  if (snapped) h = Math.round((h + resizeOverhead) / MODULE) * MODULE - resizeOverhead
  const minBodyHeight = Math.max(MODULE, Math.ceil(resizeOverhead / MODULE) * MODULE) - resizeOverhead
  h = Math.max(minBodyHeight, Math.round(h))
  h = Math.min(h, Math.max(minBodyHeight, Math.round(maxBodyHeight)))
  return h
}
