import { get, writable } from 'svelte/store'
import { workspaces, activeWorkspaceId, restoreWorkspaces, allLeaves } from './layout.js'
import { postToCs, postStateSnapshot } from '../lib/ipc.js'

// Configurable in the settings panel — how many past states to keep.
export const undoLimit = writable(10)

let stack     = []   // past states, oldest → newest; each is {workspaces, activeWorkspaceId}
let baseline  = snapshot()
let timer     = null
let suppressed = false

function snapshot() {
  return { workspaces: get(workspaces), activeWorkspaceId: get(activeWorkspaceId) }
}

// Any single user action (rename, drag-drop, capture, delete, ...) can fire
// several store updates in quick succession (e.g. a live-reorder drag). We
// only want ONE undo step per action, so we wait for things to go quiet
// before recording — this collapses a whole gesture into one step, and
// naturally skips continuous things like slider dragging mid-motion.
const SETTLE_MS = 400

function settle() {
  if (suppressed) return
  const current = snapshot()
  if (JSON.stringify(current.workspaces) === JSON.stringify(baseline.workspaces)) return
  stack.push(baseline)
  const limit = get(undoLimit)
  while (stack.length > limit) stack.shift()
  baseline = current
}

workspaces.subscribe(() => {
  if (suppressed) return
  clearTimeout(timer)
  timer = setTimeout(settle, SETTLE_MS)
})

export function canUndo() {
  return stack.length > 0
}

// Run fn (a programmatic state replacement — undo, or loading a file) without
// recording it as a user action. Also resets baseline afterward so the next
// real user action diffs against the state fn just installed, not stale state.
export function suppressDuring(fn) {
  suppressed = true
  fn()
  baseline = snapshot()
  suppressed = false
}

// Undo only swaps the UI's own copy of the state — GH never hears about a
// value that changed as part of it (slider_change etc. are only posted by the
// row's own edit path). So diff control values across the swap and push the
// ones that differ. Keyed by sourceId (copies share it, see Pane.svelte's
// sourceIdFor) — one message per underlying GH object. multiSelect lists and
// non-value types (trigger/geometry/dataDam/button) are skipped: their
// "value" isn't a plain scalar C# can be set to directly.
const MSG_TYPE = { panel: 'panel_change', colourPicker: 'colour_change' }
const SKIP_TYPES = new Set(['button', 'pancakeButton', 'trigger', 'geometryParam', 'dataDam', 'paramViewer'])

function collectControlValues(workspaceList, out = new Map()) {
  const visit = (sliders, groups) => {
    for (const s of sliders ?? []) {
      if (SKIP_TYPES.has(s.type) || s.multiSelect) continue
      out.set(s.sourceId ?? s.id, { type: s.type, value: s.value })
    }
    for (const g of groups ?? []) visit(g.sliders, g.groups)
  }
  for (const w of workspaceList)
    for (const leaf of allLeaves(w.layout))
      for (const t of leaf.tabs ?? []) visit(t.sliders, t.groups)
  return out
}

function pushChangedValues(before, after) {
  const was = collectControlValues(before)
  for (const [id, { type, value }] of collectControlValues(after)) {
    const old = was.get(id)
    if (!old || old.value === value || value === undefined) continue
    postToCs({ type: MSG_TYPE[type] ?? 'slider_change', id, value })
  }
}

export function undo() {
  clearTimeout(timer)
  settle()   // flush a pending-but-not-yet-recorded change first, so undo always reverts the last real action
  const prev = stack.pop()
  if (!prev) return
  const before = get(workspaces)
  suppressDuring(() => restoreWorkspaces(prev.workspaces, prev.activeWorkspaceId))
  pushChangedValues(before, get(workspaces))
  postStateSnapshot()
}
