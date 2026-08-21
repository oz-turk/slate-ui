<script>
  import { setRatio } from '../stores/layout.js'

  export let dir      // 'h' = vertical bar (left|right), 'v' = horizontal bar (top|bottom)
  export let splitId

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
    const raw  = dir === 'h'
      ? (e.clientX - rect.left) / rect.width
      : (e.clientY - rect.top)  / rect.height
    setRatio(splitId, Math.max(0.1, Math.min(0.9, raw)))
  }

  function onPointerUp() { dragging = false }
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
