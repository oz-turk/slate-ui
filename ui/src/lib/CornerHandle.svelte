<script>
  import { createEventDispatcher } from 'svelte'
  import { hoverHint, altHeld } from '../stores/uiState.js'

  export let corner   // 'tl' | 'tr' | 'bl' | 'br'

  const dispatch = createEventDispatcher()

  let el
  let startX, startY
  let active = false
  let lockedDir    = null
  let lockedSide   = null
  let lockedInward = null

  // Spanning (Alt+drag) only works from the 4 corners of the WHOLE window,
  // not every pane's own corner — see the checkWindowCorner() gate in
  // onPointerMove. The alt-highlight/alt-dim classes below mirror that same
  // check so the visual hint matches what's actually draggable. Measured
  // geometrically (not from tree position) since any leaf can end up sitting
  // at a window edge depending on how the tree happens to be carved up.
  // .layout-root has 3px of padding on left/right/bottom (App.svelte), so a
  // pane genuinely at the window edge still sits ~3px inset from the root's
  // own border box on those sides — the tolerance has to clear that gap.
  const EDGE_EPS = 5
  let atWindowCorner = false
  function checkWindowCorner() {
    if (!el) return false
    const root = document.querySelector('.layout-root')
    if (!root) return false
    const r = el.getBoundingClientRect()
    const w = root.getBoundingClientRect()
    const atLeft   = Math.abs(r.left   - w.left)   < EDGE_EPS
    const atRight  = Math.abs(r.right  - w.right)  < EDGE_EPS
    const atTop    = Math.abs(r.top    - w.top)    < EDGE_EPS
    const atBottom = Math.abs(r.bottom - w.bottom) < EDGE_EPS
    if (corner === 'tl') return atLeft  && atTop
    if (corner === 'tr') return atRight && atTop
    if (corner === 'bl') return atLeft  && atBottom
    return atRight && atBottom // 'br'
  }
  $: if (el) {
    if ($altHeld) atWindowCorner = checkWindowCorner()
    else          atWindowCorner = false
  }

  function getIntent(dx, dy) {
    const dist = Math.sqrt(dx * dx + dy * dy)
    if (dist < 8) return null
    const horiz  = Math.abs(dx) > Math.abs(dy)
    const inward = horiz
      ? (corner.includes('l') ? dx > 0 : dx < 0)
      : (corner.includes('t') ? dy > 0 : dy < 0)
    return {
      dir:  horiz ? 'h' : 'v',
      side: horiz ? (corner.includes('l') ? 'before' : 'after')
                  : (corner.includes('t') ? 'before' : 'after'),
      inward
    }
  }

  function onPointerDown(e) {
    e.preventDefault()
    e.stopPropagation()
    startX = e.clientX
    startY = e.clientY
    active = true
    lockedDir = lockedSide = lockedInward = null
    el.setPointerCapture(e.pointerId)
  }

  function onPointerMove(e) {
    if (!active) return
    const dx = e.clientX - startX
    const dy = e.clientY - startY

    if (lockedDir === null) {
      const intent = getIntent(dx, dy)
      if (!intent) { dispatch('preview', null); return }
      lockedDir    = intent.dir
      lockedSide   = intent.side
      lockedInward = intent.inward
    }

    if (lockedInward) {
      // Spanning is gated on this handle actually being a window corner right
      // now, not just on Alt being held — otherwise Alt+drag from an interior
      // pane corner could span the window too, which defeats the point of
      // only hinting the 4 window corners as the spanning entry point.
      const spanning = e.altKey && checkWindowCorner()
      dispatch('preview', { kind: 'split', dir: lockedDir, side: lockedSide, clientX: e.clientX, clientY: e.clientY, spanning })
    } else {
      dispatch('preview', { kind: 'collapse' })
    }
  }

  function onPointerUp(e) {
    if (!active) return
    active = false
    if (lockedDir !== null) {
      if (lockedInward) {
        dispatch('commit', { kind: 'split', dir: lockedDir, side: lockedSide, clientX: e.clientX, clientY: e.clientY })
      } else {
        dispatch('commit', { kind: 'collapse', dir: lockedDir, side: lockedSide })
      }
    }
    lockedDir = lockedSide = lockedInward = null
    dispatch('preview', null)
  }

  function onPointerCancel() {
    active = false
    lockedDir = lockedSide = lockedInward = null
    dispatch('preview', null)
  }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div
  class="corner {corner}"
  class:active
  class:alt-highlight={atWindowCorner && !active}
  class:alt-dim={$altHeld && !atWindowCorner && !active}
  bind:this={el}
  on:pointerdown={onPointerDown}
  on:pointermove={onPointerMove}
  on:pointerup={onPointerUp}
  on:pointercancel={onPointerCancel}
  on:mouseenter={() => !active && hoverHint.set('Drag inward: split this pane (hold Alt to span the whole window)  ·  drag outward: collapse a neighbour')}
  on:mouseleave={() => !active && hoverHint.set(null)}
>
  <svg viewBox="0 0 6 6" fill="currentColor" stroke="currentColor" stroke-width="1" stroke-linejoin="round" stroke-linecap="round">
    {#if corner === 'tl'}<polygon points="0,0 6,0 0,6"/>{/if}
    {#if corner === 'tr'}<polygon points="0,0 6,0 6,6"/>{/if}
    {#if corner === 'bl'}<polygon points="0,6 6,6 0,0"/>{/if}
    {#if corner === 'br'}<polygon points="6,0 0,6 6,6"/>{/if}
  </svg>
</div>

<style>
  .corner {
    position: absolute;
    width: 20px;
    height: 20px;
    z-index: 20;
    cursor: crosshair;
    color: rgba(var(--text-rgb), 0.43);
    opacity: 0.3;
    transition: opacity 0.12s, color 0.12s;
  }
  .corner:hover, .corner.active { opacity: 1; color: rgba(var(--text-rgb), 0.78); }
  .corner.alt-highlight { opacity: 0.8; color: rgba(255, 255, 255, 0.9); }
  /* While Alt is held (spanning-drag mode), only the 4 true window-corner
     triangles should read as visible — every other pane's own corners still
     work as a drag entry point (untouched), they just stop showing the glyph
     so the window doesn't look like it has 16 handles instead of 4. */
  .corner.alt-dim { opacity: 0; }
  .corner.tl { top: 0;    left: 0;  }
  .corner.tr { top: 0;    right: 0; }
  .corner.bl { bottom: 0; left: 0;  }
  .corner.br { bottom: 0; right: 0; }

  /* Anchored right at the true corner (not centred in the hit area) so it
     doesn't sit on top of the floating tab pills — the 20x20 box above is
     just the grab target, generous for the pointer, the glyph itself hugs
     the actual pixel corner with a small margin. */
  svg { position: absolute; width: 6px; height: 6px; pointer-events: none; }
  .corner.tl svg { top: 3px;    left: 3px;  }
  .corner.tr svg { top: 3px;    right: 3px; }
  .corner.bl svg { bottom: 3px; left: 3px;  }
  .corner.br svg { bottom: 3px; right: 3px; }
</style>
