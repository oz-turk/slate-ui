import { get } from 'svelte/store'
import { workspaces, allLeaves } from './layout.js'
import { scenes, activeSceneId, modifiedIds } from './sceneStore.js'
import { postToCs, postStateSnapshot, markDirty } from '../lib/ipc.js'

// Scene = { id, name, values: { [sourceId]: value } }.
//
// v1 records only controls whose value is a plain, settable value: slider,
// toggle, single-select value list / item picker / Item Selector, colour
// picker, MD slider, and Text Panels that hold typed input. Buttons, Trigger,
// Data Dam, geometry, previews, Param Viewer / Path Mapper (actions or
// visualisation, not values), multi-select lists, wired (read-only) panels
// and Graph Mapper are left out.
const KIND_BY_TYPE = {
  slider: 'num', toggle: 'num', valueList: 'num', itemPicker: 'num', humanValueList: 'num',
  colourPicker: 'colour', mdSlider: 'md', panel: 'panel'
}

function recordable(s) {
  const kind = KIND_BY_TYPE[s.type ?? 'slider']
  if (!kind || s.value === undefined || s.value === null) return null
  if (s.multiSelect) return null
  if (s.type === 'panel' && s.readOnly) return null
  return kind
}

// Live value of every recordable control, keyed by sourceId (Alt-drag copies
// share it, so one entry per underlying GH object).
function collectCurrent() {
  const out = {}
  const visit = (sliders, groups) => {
    for (const s of sliders ?? []) {
      const kind = recordable(s)
      if (kind) out[s.sourceId ?? s.id] = { kind, value: s.value }
    }
    for (const g of groups ?? []) visit(g.sliders, g.groups)
  }
  for (const w of get(workspaces))
    for (const leaf of allLeaves(w.layout))
      for (const t of leaf.tabs ?? []) visit(t.sliders, t.groups)
  return out
}

// Every control id present in the Slate tree, whatever its type.
function collectAllIds() {
  const ids = new Set()
  const visit = (sliders, groups) => {
    for (const s of sliders ?? []) ids.add(s.sourceId ?? s.id)
    for (const g of groups ?? []) visit(g.sliders, g.groups)
  }
  for (const w of get(workspaces))
    for (const leaf of allLeaves(w.layout))
      for (const t of leaf.tabs ?? []) visit(t.sliders, t.groups)
  return ids
}

// Drops scene entries whose control is no longer in Slate (deleted from the
// canvas or from Slate). Run once when a file is loaded, NOT on every save:
// pruning on save would lose the value if the control comes back through undo.
// By the time restore_state reaches the UI, C# has already removed controls
// with no live GH object, so the tree is the authoritative list.
export function pruneOrphanScenes() {
  const live = collectAllIds()
  scenes.update(list => {
    let changed = false
    const next = list.map(s => {
      const entries = Object.entries(s.values ?? {})
      const kept = entries.filter(([id]) => live.has(id))
      if (kept.length === entries.length) return s
      changed = true
      return { ...s, values: Object.fromEntries(kept) }
    })
    return changed ? next : list
  })
}

// Booleans (toggle state pushed from C#) travel as 0/1 — the wire format of
// slider_change / apply_scene is a number.
const norm = (kind, v) => kind === 'num' && typeof v === 'boolean' ? (v ? 1 : 0) : v

let _n = 0
const newSceneId = () => 'scene_' + Date.now() + '_' + (++_n)

// Scene list changes are saved in the .gh -> unsaved changes.
function persist() { postStateSnapshot(); markDirty() }

function currentValues() {
  const values = {}
  for (const [id, { kind, value }] of Object.entries(collectCurrent())) values[id] = norm(kind, value)
  return values
}

export function saveScene(name) {
  const scene = { id: newSceneId(), name: name?.trim() || `Scene ${get(scenes).length + 1}`, values: currentValues() }
  scenes.update(list => [...list, scene])
  activeSceneId.set(scene.id)
  persist()
  return scene
}

export function overwriteScene(id) {
  const values = currentValues()
  scenes.update(list => list.map(s => s.id === id ? { ...s, values } : s))
  activeSceneId.set(id)
  persist()
}

export function renameScene(id, name) {
  const n = name?.trim()
  if (!n) return
  scenes.update(list => list.map(s => s.id === id ? { ...s, name: n } : s))
  persist()
}

export function deleteScene(id) {
  scenes.update(list => list.filter(s => s.id !== id))
  if (get(activeSceneId) === id) activeSceneId.set(null)
  persist()
}

// Applies a scene with ONE message so C# writes every value and schedules a
// single solve. Controls that no longer exist (or whose type changed) are
// skipped, controls added after the scene was saved are left alone. Only
// values that actually differ from the live ones are sent. Returns
// { applied, skipped } where skipped counts scene entries with no matching control.
export function applyScene(id) {
  const scene = get(scenes).find(s => s.id === id)
  if (!scene) return { applied: 0, skipped: 0 }
  const { items, skipped } = diffScene(scene)
  activeSceneId.set(id)
  if (items.length) {
    pendingUntil = Date.now() + APPLY_PENDING_MS
    postToCs({ type: 'apply_scene', items })
    markDirty()   // control values change in GH
  }
  return { applied: items.length, skipped }
}

// Scene entries that differ from the live values (items) and entries whose
// control is no longer in Slate (skipped). Numbers compare with a tiny
// tolerance, everything else by content.
function sameValue(a, b) {
  if (typeof a === 'number' && typeof b === 'number') return Math.abs(a - b) < 1e-6
  return a === b || JSON.stringify(a) === JSON.stringify(b)
}

function diffScene(scene) {
  const current = collectCurrent()
  const items = []
  let skipped = 0
  for (const [cid, saved] of Object.entries(scene.values ?? {})) {
    const cur = current[cid]
    if (!cur) { skipped++; continue }
    if (sameValue(norm(cur.kind, cur.value), saved)) continue
    items.push({ id: cid, kind: cur.kind, value: saved })
  }
  return { items, skipped }
}

// "Modified" indicator. Right after an apply the UI still holds the old values
// until C# pushes the new ones back after the solve, so the scene would look
// modified for a moment; while an apply is pending the answer is false, and the
// pending state ends as soon as the live values match (or after the timeout,
// e.g. a slider that rounds the value to its step and never matches).
const APPLY_PENDING_MS = 4000
let pendingUntil = 0

export function applyPending() { return Date.now() < pendingUntil }

// One shared, debounced computation of which controls differ from the active
// scene (modifiedIds), used by the toolbar dot and the per-control dots.
// Debounced because sliders update the workspaces store at drag rate; while an
// apply is pending it reports nothing and re-checks until the pending state ends.
const EMPTY = new Set()
let refreshTimer

function setModified(next) {
  const cur = get(modifiedIds)
  if (cur.size === next.size && [...next].every(id => cur.has(id))) return   // no change, no re-render
  modifiedIds.set(next)
}

function refreshModified() {
  const scene = get(scenes).find(s => s.id === get(activeSceneId))
  if (!scene) { setModified(EMPTY); return }
  const ids = new Set(diffScene(scene).items.map(i => i.id))
  if (applyPending()) {
    if (ids.size === 0) pendingUntil = 0
    setModified(EMPTY)
    scheduleRefresh()
    return
  }
  setModified(ids)
}

function scheduleRefresh() {
  clearTimeout(refreshTimer)
  refreshTimer = setTimeout(refreshModified, 150)
}

workspaces.subscribe(scheduleRefresh)
scenes.subscribe(scheduleRefresh)
activeSceneId.subscribe(scheduleRefresh)
