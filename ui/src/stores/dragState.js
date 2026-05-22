import { writable } from 'svelte/store'
export const tabDrag  = writable(null)  // { tabId, fromPaneId, label } | null
export const itemDrag = writable(null)  // { type:'slider'|'group', id, fromPaneId, fromTabId, fromGroupId } | null
