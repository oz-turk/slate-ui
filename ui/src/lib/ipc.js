import { get } from 'svelte/store'
import { workspaces, activeWorkspaceId } from '../stores/layout.js'
import { theme } from '../stores/uiState.js'

export function postToCs(obj) {
  window.chrome?.webview?.postMessage(obj)
}

// Echoed back on every state_snapshot so C# can tell a fresh snapshot apart
// from a stale one — e.g. a resize-debounce timer (App.svelte's
// resizeSettleTimer) queued before the host document last switched, or one
// Chromium throttled while the window was hidden and only now got to run.
// Without this, a late snapshot from the PREVIOUS document could silently
// overwrite the cache SyncToDocument just correctly set for the new one
// (SlateWindow.cs's _syncEpoch is the other half of this — see its comment).
// Updated from App.svelte whenever a message carrying an `epoch` arrives
// (cleared/reset/restore_state), never incremented here — JS only mirrors
// whatever C# last told it, it doesn't decide when a switch happened.
let currentEpoch = 0
export function setEpoch(epoch) { currentEpoch = epoch }

export function postStateSnapshot() {
  postToCs({ type: 'state_snapshot', workspaces: get(workspaces), activeWorkspaceId: get(activeWorkspaceId), theme: get(theme), epoch: currentEpoch })
}
