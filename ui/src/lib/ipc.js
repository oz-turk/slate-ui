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

// Save the .gh this window belongs to (toolbar button and Ctrl+S). The
// snapshot goes first so the file gets the UI's latest state; messages are
// handled in order on the host side.
export function saveDocument() {
  postStateSnapshot()
  postToCs({ type: 'save_document' })
}
