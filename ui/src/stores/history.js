import { get, writable } from 'svelte/store'
import { workspaces, activeWorkspaceId, restoreWorkspaces } from './layout.js'

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

export function undo() {
  clearTimeout(timer)
  settle()   // flush a pending-but-not-yet-recorded change first, so undo always reverts the last real action
  const prev = stack.pop()
  if (!prev) return
  suppressDuring(() => restoreWorkspaces(prev.workspaces, prev.activeWorkspaceId))
}
