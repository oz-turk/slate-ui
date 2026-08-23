import { writable } from 'svelte/store'
export const mode   = writable('preview')
export const pinned = writable(true)

// 'dark' | 'light' — toggled in the settings panel, persisted with the rest
// of the workspace state (see ipc.js / App.svelte's restore_state handler).
export const theme = writable('dark')

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

// Escape clears selection everywhere at once — a tick counter rather than a
// per-pane request, since every Pane (not just one under the mouse) should react.
export const clearSelectionTick = writable(0)

// True while Alt is held — lets corner-handles highlight themselves as "this
// drag will act on more than just one pane" before the user even starts
// dragging. Set/cleared in App.svelte.
export const altHeld = writable(false)

// SplitIds currently moving together as an aligned-edge group drag — each
// divider is a separate component instance (they can belong to completely
// unrelated split subtrees), so this is how the one under the pointer tells
// the others to render as part of the same highlighted line. Empty when no
// group drag is in progress; set to just the dragged divider's own id once
// it's broken off (Alt / shake) mid-drag.
export const activeDragGroup = writable([])
