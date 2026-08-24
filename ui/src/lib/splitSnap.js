// Shared "unconditional" (no modifier needed) magnetic snap used by both
// SplitDivider's own drag and the corner-drag split-creation flow in
// Pane.svelte, so a freshly created split snaps exactly the same as dragging
// an already-existing divider — previously the corner-drag path only had the
// center snap inlined and never checked alignment against other dividers.

export const CENTER_SNAP_PX = 10
export const EDGE_SNAP_PX   = 8

export function ownRaw(el, dir) {
  const p = el.parentElement
  if (!p) return 0
  const pr = p.getBoundingClientRect()
  const r  = el.getBoundingClientRect()
  return dir === 'h' ? (r.left - pr.left) : (r.top - pr.top)
}

// Every other same-orientation divider's on-screen position, translated into
// the caller's own raw coordinate space (myOrigin = the caller's own
// parent's left/top) — so snapping against them works regardless of which
// split subtree they belong to. excludeEl is the divider to skip (itself, or
// null/not-yet-mounted during corner-drag before its own divider exists).
export function computeAlignedSnapTargets(dir, myOrigin, excludeEl) {
  const targets = []
  for (const other of document.querySelectorAll(`.divider.dir-${dir}`)) {
    if (other === excludeEl) continue
    const otherParent = other.parentElement
    if (!otherParent) continue
    const otherParentRect = otherParent.getBoundingClientRect()
    const otherOrigin = dir === 'h' ? otherParentRect.left : otherParentRect.top
    targets.push(otherOrigin + ownRaw(other, dir) - myOrigin)
  }
  return targets
}

// Applies the center snap, then the aligned-edge snap, to a raw position —
// first match wins, same priority order as SplitDivider's own drag.
export function snapRaw(raw, dim, targets) {
  const center = dim / 2
  if (Math.abs(raw - center) < CENTER_SNAP_PX) return { raw: center, label: ' (centered)' }
  for (const t of targets) {
    if (Math.abs(raw - t) < EDGE_SNAP_PX) return { raw: t, label: ' (aligned)' }
  }
  return { raw, label: '' }
}
