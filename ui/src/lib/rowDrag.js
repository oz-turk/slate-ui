// Shared row-drag behavior for the per-control-type "*Row.svelte" components
// (SliderRow, ToggleRow, ButtonRow, ValueListRow, PanelRow, ColourPickerRow).
// Every row is both a reorder-drag source (its own .handle, native HTML5 drag)
// and a drop target for other rows being dragged onto it — this was being
// hand-copied into each new Row component; centralized here instead.

// Small elastic follow when a row is dragged past the first/last position.
// Svelte only tracks reactive-variable assignments made directly inside the
// component that owns them, so this returns the value rather than setting it
// — call it from your own on:drag handler and assign the result to your
// local `dragTranslateY`:
//   function onHandleDrag(e) {
//     const v = dragTranslateYFor(e, rowEl, isFirst, isLast)
//     if (v !== null) dragTranslateY = v
//   }
// Returns null for the native "drag ended off-window" tick (clientX/Y both
// 0), which the caller should ignore rather than snapping to 0.
export function dragTranslateYFor(e, rowEl, isFirst, isLast) {
  if (e.clientX === 0 && e.clientY === 0) return null
  if (!rowEl) return 0
  const rect = rowEl.getBoundingClientRect()
  const cap  = 14
  if      (isLast  && e.clientY > rect.bottom) return  Math.min(e.clientY - rect.bottom, cap)
  else if (isFirst && e.clientY < rect.top)    return -Math.min(rect.top - e.clientY, cap)
  return 0
}

// Row-as-drop-target handlers — bind directly:
//   on:dragover|preventDefault={e => rowDragOver(e, dispatch)}
//   on:dragleave={e => rowDragLeave(e, dispatch)}
//   on:drop|preventDefault={e => rowDrop(e, dispatch)}
export function rowDragOver(e, dispatch) {
  e.dataTransfer.dropEffect = 'move'
  const rect = e.currentTarget.getBoundingClientRect()
  dispatch('rowDragOver', e.clientY < rect.top + rect.height / 2 ? 'before' : 'after')
}

export function rowDragLeave(e, dispatch) {
  if (!e.currentTarget.contains(e.relatedTarget)) dispatch('rowDragLeave')
}

export function rowDrop(e, dispatch) {
  const rect = e.currentTarget.getBoundingClientRect()
  dispatch('rowDrop', e.clientY < rect.top + rect.height / 2 ? 'before' : 'after')
}
