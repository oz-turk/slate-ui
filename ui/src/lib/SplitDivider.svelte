<script>
  import { setSplitSize } from '../stores/layout.js'
  import { hoverHint } from '../stores/uiState.js'

  export let dir      // 'h' = vertical bar (left|right), 'v' = horizontal bar (top|bottom)
  export let splitId

  const MIN_PANE_PX     = 60
  const MODULE          = 44   // one slider row's height — same grid PanelRow/ValueListRow snap to
  const CENTER_SNAP_PX  = 10   // magnetic pull toward dead-center — no modifier needed, always on

  let el
  let dragging = false
  let lastCtrlKey = false

  function onPointerDown(e) {
    e.preventDefault()
    dragging = true
    lastCtrlKey = false
    el.setPointerCapture(e.pointerId)
  }

  function applyMove(e, snapOverride = e.ctrlKey) {
    const parent = el.parentElement
    if (!parent) return
    const rect = parent.getBoundingClientRect()
    const dim  = dir === 'h' ? rect.width : rect.height
    let raw    = dir === 'h'
      ? (e.clientX - rect.left)
      : (e.clientY - rect.top)

    // Hidden magnetic snap to dead-center (50/50) — no modifier needed, just a
    // gentle pull when the pointer passes within a few px of the midpoint.
    // Takes priority over the row-grid snap below when both are in play.
    const center = dim / 2
    const centered = Math.abs(raw - center) < CENTER_SNAP_PX

    // Ctrl-snap to the row-height grid — horizontal dividers only (top/bottom
    // split), so stacked panes' rows land on the same grid and line up with
    // whatever's beside them, which pixel-exact dragging can't guarantee.
    const snapped = !centered && dir === 'v' && snapOverride

    if (centered) raw = center
    else if (snapped) raw = Math.round(raw / MODULE) * MODULE

    const size = Math.max(MIN_PANE_PX, Math.min(dim - MIN_PANE_PX, raw))
    setSplitSize(splitId, size)
    const label = centered ? ' (centered)' : snapped ? ' (snapped)' : ''
    hoverHint.set(`${Math.round(size)}px${label}${dir === 'v' ? '  ·  Hold Ctrl to snap to slider-row size' : ''}`)
  }

  function onPointerMove(e) {
    if (!dragging) return
    lastCtrlKey = e.ctrlKey
    applyMove(e)
  }

  function onPointerUp(e) {
    if (!dragging) return
    dragging = false
    // Recompute from the pointerup event's own coordinates — pointermove can
    // coalesce/drop under the browser, so the last onPointerMove reading can
    // lag behind the actual release point. OR the ctrlKey with the last move's
    // reading too: releasing Ctrl a hair before the mouse button is a common
    // muscle-memory pattern, and shouldn't silently drop the snap on release.
    applyMove(e, e.ctrlKey || lastCtrlKey)
    hoverHint.set(null)
  }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div
  class="divider dir-{dir}"
  class:dragging
  bind:this={el}
  on:pointerdown={onPointerDown}
  on:pointermove={onPointerMove}
  on:pointerup={onPointerUp}
  on:pointercancel={onPointerUp}
></div>

<style>
  .divider {
    background: var(--bg);
    flex-shrink: 0;
    position: relative;
    z-index: 5;
    transition: background 0.12s;
  }
  .divider::after {
    content: '';
    position: absolute;
    background: transparent;
    transition: background 0.12s;
  }
  .divider:hover, .divider.dragging { background: var(--accent); }

  .dir-h {
    width: 4px;
    cursor: ew-resize;
    align-self: stretch;
  }
  .dir-h::after { inset: 0 -4px; }

  .dir-v {
    height: 4px;
    cursor: ns-resize;
    align-self: stretch;
  }
  .dir-v::after { inset: -4px 0; }
</style>
