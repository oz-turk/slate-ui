import { writable } from 'svelte/store'
export const mode   = writable('preview')
export const pinned = writable(true)

// Status bar hint — null means "show the default, mode-based shortcut list";
// any component can set() its own hint on hover and clear it (set null) on leave.
export const hoverHint = writable(null)

// "x"/"c" hotkeys act on whatever's under the mouse, resolved via
// document.elementFromPoint() rather than wiring hover state through every
// row/pane component. App.svelte sets these; the matching Pane instance
// (found by paneId) reacts and clears it.
export const deleteRequest  = writable(null)  // { paneId, sliderId } | null
export const captureRequest = writable(null)  // paneId | null

// Toggled by the gear icon in EditToolbar.
export const settingsOpen = writable(false)
