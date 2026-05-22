import { writable } from 'svelte/store'
export const tabDrag        = writable(null)   // { tabId, fromPaneId, label } | null
export const itemDrag       = writable(null)   // { type:'slider'|'group', id, fromPaneId, fromTabId, fromGroupId } | null
export const collapsePreview = writable(null)  // paneId | null — which pane would be collapsed by current corner drag
