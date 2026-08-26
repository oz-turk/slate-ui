import { writable } from 'svelte/store'
export const tabDrag        = writable(null)   // { tabId, fromPaneId, fromWorkspaceId, label } | null
export const itemDrag       = writable(null)   // { type:'slider'|'group', id, ids, fromPaneId, fromTabId, fromGroupId, fromWorkspaceId } | null — ids holds the whole dragged block (>1 when dragging a multi-selection)
