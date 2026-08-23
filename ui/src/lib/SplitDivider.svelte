<script>
  import { setSplitSize } from '../stores/layout.js'
  import { hoverHint, activeDragGroup } from '../stores/uiState.js'

  export let dir      // 'h' = vertical bar (left|right), 'v' = horizontal bar (top|bottom)
  export let splitId

  const MIN_PANE_PX     = 60
  const MODULE          = 44   // one slider row's height — same grid PanelRow/ValueListRow snap to
  const CENTER_SNAP_PX  = 10   // magnetic pull toward dead-center — no modifier needed, always on
  const EDGE_SNAP_PX    = 8    // magnetic pull toward any other on-screen-aligned divider — same, always on

  let el
  let dragging = false
  let lastCtrlKey = false

  // Every OTHER divider of the same orientation that's aligned with this one
  // (by on-screen position, not by tree structure — two dividers can belong
  // to entirely separate split nodes and just coincidentally line up) moves
  // in lockstep with it BY DEFAULT — captured once at pointerdown and frozen
  // for the rest of the drag. Each entry carries what that divider needs to
  // reproduce this one's raw-position math independently (its own parent
  // rect + its own starting raw position), since it can live in a
  // differently-positioned and differently-sized container.
  let groupedDividers = []
  // Sticky for the rest of the drag once set — either Alt or a shake breaks
  // this divider OUT of groupedDividers, back to moving alone.
  let detached        = false
  let dragStartRaw    = 0
  const ALIGN_EPS_PX  = 3

  // Computed once at drag start — the other dividers don't move during a
  // plain (non-grouped) drag, so their targets stay valid for its duration.
  let snapTargets = []

  // Shake-to-detach: a quick back-and-forth wiggle while dragging breaks this
  // divider out of its group, same as holding Alt, for when a hand isn't
  // free for the keyboard. Tracks recent {t, raw} samples in the drag axis
  // only; triggers on 2+ direction reversals covering enough distance within
  // a short window, so an ordinary slightly-unsteady drag doesn't
  // false-positive — has to actually be a fast wiggle.
  const SHAKE_WINDOW_MS = 220
  const SHAKE_MIN_PX    = 14
  let shakeSamples = []

  function resetShake() { shakeSamples = [] }

  function rawFromEvent(e) {
    const parent = el.parentElement
    if (!parent) return 0
    const rect = parent.getBoundingClientRect()
    return dir === 'h' ? (e.clientX - rect.left) : (e.clientY - rect.top)
  }

  function detectShake(t, raw) {
    shakeSamples.push({ t, raw })
    const cutoff = t - SHAKE_WINDOW_MS
    shakeSamples = shakeSamples.filter(s => s.t >= cutoff)
    if (shakeSamples.length < 4) return false
    let reversals = 0, dist = 0, lastDir = 0
    for (let i = 1; i < shakeSamples.length; i++) {
      const d = shakeSamples[i].raw - shakeSamples[i - 1].raw
      if (Math.abs(d) < 0.5) continue
      const dir2 = d > 0 ? 1 : -1
      if (lastDir !== 0 && dir2 !== lastDir) reversals++
      dist += Math.abs(d)
      lastDir = dir2
    }
    return reversals >= 2 && dist >= SHAKE_MIN_PX
  }

  function ownRaw(divEl, dir) {
    const p = divEl.parentElement
    if (!p) return 0
    const pr = p.getBoundingClientRect()
    const r  = divEl.getBoundingClientRect()
    return dir === 'h' ? (r.left - pr.left) : (r.top - pr.top)
  }

  // Unconditional (no modifier) proximity snap — every OTHER same-orientation
  // divider's current on-screen position, translated into MY raw coordinate
  // space (my own parent's origin), so dragging near one just clicks into
  // exact alignment with it. Independent of the Alt-group feature below:
  // this only ever moves the divider actually being dragged.
  function computeSnapTargets() {
    const parent = el.parentElement
    if (!parent) return []
    const myParentRect = parent.getBoundingClientRect()
    const myOrigin = dir === 'h' ? myParentRect.left : myParentRect.top
    const targets = []
    for (const other of document.querySelectorAll(`.divider.dir-${dir}`)) {
      if (other === el) continue
      const otherParent = other.parentElement
      if (!otherParent) continue
      const otherParentRect = otherParent.getBoundingClientRect()
      const otherOrigin = dir === 'h' ? otherParentRect.left : otherParentRect.top
      targets.push(otherOrigin + ownRaw(other, dir) - myOrigin)
    }
    return targets
  }

  function findAlignedDividers() {
    const mine = el.getBoundingClientRect()
    const myCenter = dir === 'h' ? mine.left + mine.width / 2 : mine.top + mine.height / 2
    const group = []
    for (const other of document.querySelectorAll(`.divider.dir-${dir}`)) {
      if (other === el) continue
      const r = other.getBoundingClientRect()
      const otherCenter = dir === 'h' ? r.left + r.width / 2 : r.top + r.height / 2
      if (Math.abs(otherCenter - myCenter) > ALIGN_EPS_PX) continue
      const otherSplitId = other.dataset.splitId
      const parent = other.parentElement
      if (!otherSplitId || !parent) continue
      const parentRect = parent.getBoundingClientRect()
      group.push({
        splitId: otherSplitId,
        dim: dir === 'h' ? parentRect.width : parentRect.height,
        startRaw: ownRaw(other, dir),
      })
    }
    return group
  }

  function onPointerDown(e) {
    e.preventDefault()
    dragging = true
    lastCtrlKey = false
    el.setPointerCapture(e.pointerId)
    dragStartRaw    = ownRaw(el, dir)
    groupedDividers = findAlignedDividers()
    detached        = false
    snapTargets     = computeSnapTargets()
    resetShake()
    activeDragGroup.set([splitId, ...groupedDividers.map(g => g.splitId)])
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
    // Takes priority over the edge/row-grid snaps below when more than one's
    // in play.
    const center = dim / 2
    const centered = Math.abs(raw - center) < CENTER_SNAP_PX

    // Unconditional proximity snap to any other on-screen-aligned divider —
    // same "always on, no modifier" spirit as the center snap, just against
    // a moving target list instead of a fixed midpoint.
    let edgeTarget = null
    if (!centered) {
      for (const t of snapTargets) {
        if (Math.abs(raw - t) < EDGE_SNAP_PX) { edgeTarget = t; break }
      }
    }

    // Ctrl-snap to the row-height grid — horizontal dividers only (top/bottom
    // split), so stacked panes' rows land on the same grid and line up with
    // whatever's beside them, which pixel-exact dragging can't guarantee.
    const snapped = !centered && edgeTarget === null && dir === 'v' && snapOverride

    if (centered) raw = center
    else if (edgeTarget !== null) raw = edgeTarget
    else if (snapped) raw = Math.round(raw / MODULE) * MODULE

    const size = Math.max(MIN_PANE_PX, Math.min(dim - MIN_PANE_PX, raw))
    setSplitSize(splitId, size)

    // Apply the SAME net movement (in screen pixels, post-snap) to every
    // aligned divider, each measured against its own container — this is
    // what keeps them visually locked together even though they can belong
    // to completely unrelated split nodes. Skipped once detached.
    const group = detached ? [] : groupedDividers
    if (group.length) {
      const delta = size - dragStartRaw
      for (const g of group) {
        const otherSize = Math.max(MIN_PANE_PX, Math.min(g.dim - MIN_PANE_PX, g.startRaw + delta))
        setSplitSize(g.splitId, otherSize)
      }
    }

    const label = centered ? ' (centered)' : edgeTarget !== null ? ' (aligned)' : snapped ? ' (snapped)' : ''
    const groupHint = group.length ? `  ·  ${group.length + 1} aligned edges moving together`
                     : (detached && groupedDividers.length) ? '  ·  broken off'
                     : ''
    hoverHint.set(`${Math.round(size)}px${label}${groupHint}${dir === 'v' && !group.length ? '  ·  Hold Ctrl to snap to slider-row size' : ''}`)
  }

  function onPointerMove(e) {
    if (!dragging) return
    lastCtrlKey = e.ctrlKey
    // Alt or a shake breaks this divider out of its (already-frozen) group —
    // sticky for the rest of the drag once triggered, checked every move
    // since either can happen well after pointerdown.
    if (!detached && groupedDividers.length && (e.altKey || detectShake(e.timeStamp, rawFromEvent(e)))) {
      detached = true
      activeDragGroup.set([splitId])
    }
    applyMove(e)
  }

  function onPointerUp(e) {
    if (!dragging) return
    dragging = false
    if (!detached && groupedDividers.length && e.altKey) detached = true
    // Recompute from the pointerup event's own coordinates — pointermove can
    // coalesce/drop under the browser, so the last onPointerMove reading can
    // lag behind the actual release point. OR the ctrlKey with the last move's
    // reading too: releasing Ctrl a hair before the mouse button is a common
    // muscle-memory pattern, and shouldn't silently drop the snap on release.
    applyMove(e, e.ctrlKey || lastCtrlKey)
    groupedDividers = []
    detached = false
    activeDragGroup.set([])
    hoverHint.set(null)
  }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div
  class="divider dir-{dir}"
  class:dragging
  class:group-active={!dragging && $activeDragGroup.includes(splitId)}
  data-split-id={splitId}
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

  /* Part of another divider's active group drag (not the one under the
     pointer) — same accent as .dragging so the whole aligned line reads as
     one continuous edge even though it's several separate DOM elements. */
  .divider.group-active { background: var(--accent); }

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
