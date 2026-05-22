import { get } from 'svelte/store'
import { layout } from '../stores/layout.js'

export function postToCs(obj) {
  window.chrome?.webview?.postMessage(obj)
}

export function postStateSnapshot() {
  postToCs({ type: 'state_snapshot', layout: get(layout) })
}
