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

// header/margin/handle/padding/border around the resizable body is not
// itself a multiple of MODULE, so snapping the body alone can never land the
// row's outer edge on the grid — both measured live (not hardcoded) so they
// keep working if the surrounding layout ever changes.
export function measureResizeBounds(rowEl, startHeight) {
  const resizeOverhead = rowEl ? rowEl.offsetHeight - startHeight : 0
  const contentEl = rowEl?.closest('.content')
  const maxBodyHeight = contentEl && rowEl
    ? contentEl.getBoundingClientRect().bottom - rowEl.getBoundingClientRect().top - resizeOverhead
    : Infinity
  return { resizeOverhead, maxBodyHeight }
}

// New body height for the pointer's travel since resize-start, optionally
// snapped to whole MODULE increments, clamped to [MODULE, maxBodyHeight].
export function computeResizeHeight({ clientY, resizeStartY, resizeStartH, resizeOverhead, maxBodyHeight, snapped, MODULE }) {
  let h = resizeStartH + (clientY - resizeStartY)
  if (snapped) h = Math.round((h + resizeOverhead) / MODULE) * MODULE - resizeOverhead
  h = Math.max(MODULE, Math.round(h))
  h = Math.min(h, Math.max(MODULE, Math.round(maxBodyHeight)))
  return h
}
