import { get, writable } from 'svelte/store'
import { workspaces, activeWorkspaceId, restoreWorkspaces, allLeaves } from './layout.js'
import { postToCs, postStateSnapshot, markDirty } from '../lib/ipc.js'

// Configurable in the settings panel — how many past states to keep.
export const undoLimit = writable(50)

let stack     = []   // past states, oldest → newest; each is {workspaces, activeWorkspaceId}
let baseline, baselineJson   // last recorded state + its workspaces JSON (compared often, so cached)
let timer     = null
let suppressed = false

function snapshot() {
  return { workspaces: get(workspaces), activeWorkspaceId: get(activeWorkspaceId) }
}
function setBaseline(state = snapshot(), json = JSON.stringify(state.workspaces)) {
  baseline = state
  baselineJson = json
}

// Any single user action (rename, drag-drop, capture, delete, ...) can fire
// several store updates in quick succession (e.g. a live-reorder drag). We
// only want ONE undo step per action, so we wait for things to go quiet
// before recording — this collapses a whole gesture into one step, and
// naturally skips continuous things like slider dragging mid-motion.
const SETTLE_MS = 400

// Unsaved-changes flag (.gh `*`, see ipc.js markDirty). Sent as soon as the
// state first differs from the baseline, not when the step settles: GH-side
// syncs after the change's solve keep restarting the settle timer, which
// delayed the `*` by 1-2 s. At most once per settle window, so a slider drag
// costs one JSON compare, not one per frame. Changes made only by a
// window-edge resize (asResizeChange) don't count: they rescale the layout,
// the user didn't edit anything. They still become an undo step.
let editedSinceSettle = false   // a non-resize change happened in this window
let dirtySent = false           // markDirty already sent in this window
let inResize = false
export function asResizeChange(fn) {
  inResize = true
  try { fn() } finally { inResize = false }
}

function differsFromBaseline() {
  return get(activeWorkspaceId) !== baseline.activeWorkspaceId ||
         JSON.stringify(get(workspaces)) !== baselineJson
}

function settle() {
  if (suppressed) return
  const edited = editedSinceSettle, sent = dirtySent
  editedSinceSettle = false
  dirtySent = false
  const current = snapshot()
  const json = JSON.stringify(current.workspaces)
  if (current.activeWorkspaceId === baseline.activeWorkspaceId && json === baselineJson) return
  stack.push(baseline)
  const limit = get(undoLimit)
  while (stack.length > limit) stack.shift()
  setBaseline(current, json)
  if (edited && !sent) markDirty()   // fallback; normally already sent in scheduleSettle
}

// Switching workspaces is a step of its own, like switching tabs (activeTabId
// lives inside workspaces). Otherwise the baseline kept the old workspace and
// undoing a change made after a switch jumped back to it.
function scheduleSettle() {
  if (suppressed) return
  if (!inResize) {
    editedSinceSettle = true
    if (!dirtySent && differsFromBaseline()) { dirtySent = true; markDirty() }
  }
  clearTimeout(timer)
  timer = setTimeout(settle, SETTLE_MS)
}
setBaseline()
workspaces.subscribe(scheduleSettle)
activeWorkspaceId.subscribe(scheduleSettle)

export function canUndo() {
  return stack.length > 0
}

// Run fn (a programmatic state replacement — undo, or loading a file) without
// recording it as a user action. Also resets baseline afterward so the next
// real user action diffs against the state fn just installed, not stale state.
export function suppressDuring(fn) {
  suppressed = true
  fn()
  setBaseline()
  dirtySent = false   // e.g. switched to another .gh: its first change must reach C# too
  suppressed = false
}

// Undo only swaps the UI's own copy of the state — GH never hears about a
// value that changed as part of it (slider_change etc. are only posted by the
// row's own edit path). So diff control values across the swap and push the
// ones that differ. Keyed by sourceId (copies share it, see Pane.svelte's
// sourceIdFor) — one message per underlying GH object. multiSelect lists and
// non-value types (trigger/geometry/dataDam/button) are skipped: their
// "value" isn't a plain scalar C# can be set to directly.
const MSG_TYPE = { panel: 'panel_change', colourPicker: 'colour_change', mdSlider: 'md_change', graphMapper: 'graph_change' }
const SKIP_TYPES = new Set(['button', 'pancakeButton', 'trigger', 'geometryParam', 'dataDam', 'customPreview', 'paramViewer'])

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
    if (!old || value === undefined) continue
    // MD slider's value is an { x, y } object — compare by content, not identity
    if (old.value === value || JSON.stringify(old.value) === JSON.stringify(value)) continue
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
  markDirty()
}
