<script>
  import { setSplitSize } from '../stores/layout.js'
  import { hoverHint } from '../stores/uiState.js'

  export let dir      // 'h' = vertical bar (left|right), 'v' = horizontal bar (top|bottom)
  export let splitId

  const MIN_PANE_PX = 60
  const MODULE       = 44   // one slider row's height — same grid PanelRow/ValueListRow snap to

  let el
  let dragging = false

  function onPointerDown(e) {
    e.preventDefault()
    dragging = true
    el.setPointerCapture(e.pointerId)
  }

  function onPointerMove(e) {
    if (!dragging) return
    const parent = el.parentElement
    if (!parent) return
    const rect = parent.getBoundingClientRect()
    const dim  = dir === 'h' ? rect.width : rect.height
    let raw    = dir === 'h'
      ? (e.clientX - rect.left)
      : (e.clientY - rect.top)

    // Ctrl-snap to the row-height grid — horizontal dividers only (top/bottom
    // split), so stacked panes' rows land on the same grid and line up with
    // whatever's beside them, which pixel-exact dragging can't guarantee.
    const snapped = dir === 'v' && e.ctrlKey
    if (snapped) raw = Math.round(raw / MODULE) * MODULE

    const size = Math.max(MIN_PANE_PX, Math.min(dim - MIN_PANE_PX, raw))
    setSplitSize(splitId, size)
    hoverHint.set(dir === 'v' ? `${Math.round(size)}px${snapped ? ' (snapped)' : ''}  ·  Hold Ctrl to snap to slider-row size` : null)
  }

  function onPointerUp() {
    dragging = false
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
