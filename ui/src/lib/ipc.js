import { get } from 'svelte/store'
import { workspaces, activeWorkspaceId } from '../stores/layout.js'
import { theme } from '../stores/uiState.js'

export function postToCs(obj) {
  window.chrome?.webview?.postMessage(obj)
}

export function postStateSnapshot() {
  postToCs({ type: 'state_snapshot', workspaces: get(workspaces), activeWorkspaceId: get(activeWorkspaceId), theme: get(theme) })
}
