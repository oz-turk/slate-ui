import { get } from 'svelte/store'
import { workspaces, activeWorkspaceId } from '../stores/layout.js'
import { theme, showParamIcons } from '../stores/uiState.js'
import { scenes } from '../stores/sceneStore.js'

export function postToCs(obj) {
  window.chrome?.webview?.postMessage(obj)
}

export function postStateSnapshot() {
  postToCs({ type: 'state_snapshot', workspaces: get(workspaces), activeWorkspaceId: get(activeWorkspaceId), theme: get(theme), showIcons: get(showParamIcons), scenes: get(scenes) })
}

// Tells C# a real user change happened, so it marks the .gh modified (GH's
// `*`, and the "save changes?" prompt on close). The UI decides, not C#: only
// the UI can tell a user action from a file load, a window resize or a
// GH-side sync, which all post the same state_snapshot. Callers: history.js
// (every recorded undo step, and undo itself) and scenes.js.
export function markDirty() {
  postToCs({ type: 'mark_dirty' })
}

// Save the .gh this window belongs to (toolbar button and Ctrl+S). The
// snapshot goes first so the file gets the UI's latest state; messages are
// handled in order on the host side.
export function saveDocument() {
  postStateSnapshot()
  postToCs({ type: 'save_document' })
}
