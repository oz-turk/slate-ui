import { writable, derived } from 'svelte/store'

// Named snapshots of control values ("scenes"), stored per Grasshopper
// document: [{ id, name, values: { [sourceId]: value } }]. Kept in its own
// tiny module (store only, no imports from ipc/layout) so ipc.js can include
// it in the state snapshot without an import cycle with scenes.js.
export const scenes = writable([])

// Last scene applied or saved in this session. Not persisted.
export const activeSceneId = writable(null)

// Scene whose apply is still solving in GH (spinner in the Scenes menu);
// cleared by C#'s scene_applied once the solve it triggered has finished.
export const applyingSceneId = writable(null)

// sourceIds of the controls whose live value differs from the active scene.
// Kept up to date by scenes.js (debounced); rows only read it.
export const modifiedIds = writable(new Set())
export const sceneModified = derived(modifiedIds, $ids => $ids.size > 0)
