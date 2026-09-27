import { writable } from 'svelte/store'
export const mode   = writable('preview')
export const pinned = writable(true)

// 'dark' | 'light' — toggled in the settings panel, persisted two
// ways: per-file with the rest of the workspace state (ipc.js / App.svelte's
// restore_state handler takes priority when a file has its own saved theme),
// and as a cross-file default in localStorage (WebView2's profile — see
// SlateWindow.cs's userDataFolder — outlives any single .gh file) so a file
// with no saved theme opens in whatever the user picked last, not always dark.
// (a third 'beta' theme existed 2026-09-22..09-24 as a testbed for the
// pane background/pattern feature — removed once that feature graduated to
// dark/light directly; an old file/localStorage value of 'beta' just falls
// through the THEMES.includes() check below to 'dark'.)
const THEMES = ['dark', 'light']
function readDefaultTheme() {
  try {
    const t = localStorage.getItem('slate-theme')
    return THEMES.includes(t) ? t : 'dark'
  } catch {
    return 'dark'
  }
}
export const theme = writable(readDefaultTheme())

// Status bar hint — null means "show the default, mode-based shortcut list";
// any component can set() its own hint on hover and clear it (set null) on leave.
export const hoverHint = writable(null)

// "x"/"c" hotkeys act on whatever's under the mouse, resolved via
// document.elementFromPoint() rather than wiring hover state through every
// row/pane component. App.svelte sets these; the matching Pane instance
// (found by paneId) reacts and clears it.
export const deleteRequest  = writable(null)  // { paneId, sliderId } | { paneId, groupId } | null
export const captureRequest = writable(null)  // { paneId, groupId } | null — groupId null means "capture to the tab, not a specific group"

// Toggled by the gear icon in EditToolbar.
export const settingsOpen = writable(false)

// Escape clears selection everywhere at once — a tick counter rather than a
// per-pane request, since every Pane (not just one under the mouse) should react.
export const clearSelectionTick = writable(0)

// "g" groups the current selection — same broadcast-tick shape as
// clearSelectionTick above (not a paneId-targeted request like delete/capture-
// Request) since it acts on whatever Pane already holds a selection, not
// whatever's under the mouse.
export const groupSelectionTick = writable(0)

// True while Alt is held — lets corner-handles highlight themselves as "this
// drag will act on more than just one pane" before the user even starts
// dragging. Set/cleared in App.svelte.
export const altHeld = writable(false)

// True while Ctrl (or Cmd) is held — StatusBar swaps its default hint list
// for the Ctrl-specific shortcuts (multi/range-select, undo, workspace
// switch) while it's down. Set/cleared in App.svelte alongside altHeld.
export const ctrlHeld = writable(false)

// Id of the paramViewer control currently shown fullscreen (covers the whole
// Slate window, not the OS screen) — null when closed. Set by
// ParamViewerRow's expand button, cleared by ParamViewerFullscreen's close
// button or Escape (see App.svelte).
export const fullscreenTreeId = writable(null)

// SplitIds currently moving together as an aligned-edge group drag — each
// divider is a separate component instance (they can belong to completely
// unrelated split subtrees), so this is how the one under the pointer tells
// the others to render as part of the same highlighted line. Empty when no
// group drag is in progress; set to just the dragged divider's own id once
// it's broken off (Alt / shake) mid-drag.
export const activeDragGroup = writable([])

// On-demand cache for the Data Tree Explorer's item-view drill-in (drilling
// all the way into a real branch to read its actual values, rendered as
// its own ring — see DataTreeSunburst.svelte's itemsPath/itemsNode).
// Keyed by `${paramViewerId}:${path}`; filled by App.svelte's
// 'paramViewer_items_result' handler. Deliberately not persisted/read by
// state_snapshot — always a fresh request per drill, GH data can change
// between opens and a branch can hold thousands of items.
export const paramViewerItemsCache = writable({})
