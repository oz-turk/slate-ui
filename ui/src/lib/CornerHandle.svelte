<script>
  import { createEventDispatcher } from 'svelte'
  import { altHeld } from '../stores/uiState.js'

  export let corner   // 'tl' | 'tr' | 'bl' | 'br'

  const dispatch = createEventDispatcher()

  let el
  let startX, startY
  let active = false
  let lockedDir    = null
  let lockedSide   = null
  let lockedInward = null

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
      // Alt held = spanning split (pushes the whole window aside, not just
      // the one pane) — read live so toggling Alt mid-drag still applies at
      // the moment the split actually fires (which, per Pane.svelte's
      // splitInFlight guard, is on the FIRST preview after intent locks —
      // in practice that's within ~8px of pointerdown, so Alt needs to
      // already be held by then; pressing it after doesn't retroactively
      // apply).
      dispatch('preview', { kind: 'split', dir: lockedDir, side: lockedSide, clientX: e.clientX, clientY: e.clientY, spanning: e.altKey })
    } else {
      dispatch('preview', { kind: 'collapse' })
    }
  }

  function onPointerUp(e) {
    if (!active) return
    active = false
    if (lockedDir !== null) {
      if (lockedInward) {
        dispatch('commit', { kind: 'split', dir: lockedDir, side: lockedSide, clientX: e.clientX, clientY: e.clientY, spanning: e.altKey })
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
  class:alt-highlight={$altHeld && !active}
  bind:this={el}
  on:pointerdown={onPointerDown}
  on:pointermove={onPointerMove}
  on:pointerup={onPointerUp}
  on:pointercancel={onPointerCancel}
>
  <svg viewBox="0 0 6 6" fill="currentColor">
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
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .corner:hover, .corner.active { opacity: 1; color: rgba(var(--text-rgb), 0.78); }
  .corner.alt-highlight { opacity: 0.8; color: rgba(255, 255, 255, 0.9); }
  .corner.tl { top: 0;    left: 0;  }
  .corner.tr { top: 0;    right: 0; }
  .corner.bl { bottom: 0; left: 0;  }
  .corner.br { bottom: 0; right: 0; }
  svg { width: 8px; height: 8px; pointer-events: none; }
</style>
