import { writable, derived, get } from 'svelte/store'

// ── ID generators ─────────────────────────────────────────────────────────────
let _pc = 0, _sc = 0, _tc = 0, _wc = 0
const pid  = () => 'pane_'  + (++_pc)
const sid  = () => 'split_' + (++_sc)
const wid  = () => 'ws_'    + (++_wc)
export const newTabId = () => 'tab_' + Date.now() + '_' + (++_tc)
export const newSplitId = () => sid()

// Restoring a saved file hands back ids that were minted by a PAST page
// session's own counters (e.g. "ws_2", "pane_5") — this session's _pc/_sc/_wc
// start back at 0, so the next locally-generated id can collide with one
// that's already in the restored tree (classic case: a file saved with two
// workspaces "ws_1"/"ws_2" is reopened, and clicking "+" mints "ws_1" again).
// A duplicate id breaks the {#each ... (id)} keyed block it lands in — that
// component stops updating, which reads as the whole panel going unresponsive
// for anything touching workspaces (switching, adding, capturing) while
// unrelated state (e.g. edit/preview mode) keeps working fine. Bumping each
// counter past the highest restored number closes that gap.
function bumpCounterPastId(id, prefix, current) {
  if (typeof id !== 'string' || !id.startsWith(prefix)) return current
  const n = parseInt(id.slice(prefix.length), 10)
  return Number.isFinite(n) && n > current ? n : current
}
function reconcileCounters(node) {
  if (!node) return
  if (node.type === 'leaf') { _pc = bumpCounterPastId(node.paneId, 'pane_', _pc); return }
  if (node.type === 'split') {
    _sc = bumpCounterPastId(node.splitId, 'split_', _sc)
    reconcileCounters(node.a)
    reconcileCounters(node.b)
  }
}

// Smallest "{prefix} N" (N >= 1) not already present in existingLabels — so
// naming fills gaps left by deletions instead of climbing forever.
function nextAvailableName(existingLabels, prefix) {
  const used = new Set()
  const re = new RegExp('^' + prefix + ' (\\d+)$')
  for (const label of existingLabels) {
    const m = re.exec(label)
    if (m) used.add(parseInt(m[1], 10))
  }
  let n = 1
  while (used.has(n)) n++
  return prefix + ' ' + n
}

// ── constructors ──────────────────────────────────────────────────────────────
export function makeLeaf(tabs, activeTabId) {
  if (!tabs) {
    const id = newTabId()
    tabs = [{ id, label: 'Main', sliders: [], groups: [] }]
    activeTabId = id
  }
  return { type: 'leaf', paneId: pid(), tabs, activeTabId: activeTabId ?? tabs[0]?.id ?? null }
}

// sizeA = pixel width/height of pane `a` (the earlier/top-or-left side); `b` is
// always flex:1 and absorbs the rest — so growing the window only grows `b`.
// presetId lets a caller know the new split's id before the store update
// resolves (see splitPane's own presetId param).
function makeSplit(dir, a, b, sizeA = 260, presetId) {
  return { type: 'split', splitId: presetId ?? sid(), dir, sizeA, a, b }
}

// ── store ─────────────────────────────────────────────────────────────────────
// Each workspace is a fully independent pane/split/tab tree — switching the
// active one swaps the whole layout, like a separate desktop.
const _initialWorkspaceId = wid()
export const workspaces = writable([
  { id: _initialWorkspaceId, label: 'Workspace 1', layout: makeLeaf() }
])
export const activeWorkspaceId = writable(_initialWorkspaceId)

// `layout` stays a store holding just the ACTIVE workspace's tree, so every
// existing read site ($layout / get(layout)) keeps working unchanged.
export const layout = derived(
  [workspaces, activeWorkspaceId],
  ([$workspaces, $activeId]) => $workspaces.find(w => w.id === $activeId)?.layout ?? $workspaces[0]?.layout
)

// All the tree-mutating exports below funnel through this instead of a plain
// `layout.set/update`, so they transparently operate on whichever workspace
// is currently active.
function updateActiveLayout(fn) {
  const activeId = get(activeWorkspaceId)
  workspaces.update(list => list.map(w => w.id === activeId ? { ...w, layout: fn(w.layout) } : w))
}

// ── tree helpers ──────────────────────────────────────────────────────────────
export function findLeaf(node, paneId) {
  if (node.type === 'leaf') return node.paneId === paneId ? node : null
  return findLeaf(node.a, paneId) ?? findLeaf(node.b, paneId)
}

export function allLeaves(node) {
  if (node.type === 'leaf') return [node]
  return [...allLeaves(node.a), ...allLeaves(node.b)]
}

function mapLeaf(node, paneId, fn) {
  if (node.type === 'leaf') return node.paneId === paneId ? { ...node, ...fn(node) } : node
  return { ...node, a: mapLeaf(node.a, paneId, fn), b: mapLeaf(node.b, paneId, fn) }
}

function mapSplit(node, splitId, fn) {
  if (node.type === 'leaf') return node
  if (node.splitId === splitId) return { ...node, ...fn(node) }
  return { ...node, a: mapSplit(node.a, splitId, fn), b: mapSplit(node.b, splitId, fn) }
}

function doSplit(node, paneId, dir, side, sizeA, label, presetId) {
  if (node.type === 'leaf') {
    if (node.paneId !== paneId) return node
    const tabId = newTabId()
    const fresh = makeLeaf([{ id: tabId, label, sliders: [], groups: [] }], tabId)
    const [a, b] = side === 'before' ? [fresh, node] : [node, fresh]
    return makeSplit(dir, a, b, sizeA, presetId)
  }
  return { ...node, a: doSplit(node.a, paneId, dir, side, sizeA, label, presetId), b: doSplit(node.b, paneId, dir, side, sizeA, label, presetId) }
}

function doCollapse(node, paneId) {
  if (node.type === 'leaf') return node
  if (node.a.type === 'leaf' && node.a.paneId === paneId) return node.b
  if (node.b.type === 'leaf' && node.b.paneId === paneId) return node.a
  return { ...node, a: doCollapse(node.a, paneId), b: doCollapse(node.b, paneId) }
}

// Extract tab from a pane, return [newNode, tab | null]
function doExtractTab(node, paneId, tabId) {
  let tab = null
  function walk(n) {
    if (n.type !== 'leaf' || n.paneId !== paneId) {
      if (n.type === 'split') return { ...n, a: walk(n.a), b: walk(n.b) }
      return n
    }
    tab = n.tabs.find(t => t.id === tabId) ?? null
    const tabs = n.tabs.filter(t => t.id !== tabId)
    const activeTabId = n.activeTabId === tabId ? (tabs[0]?.id ?? null) : n.activeTabId
    return { ...n, tabs, activeTabId }
  }
  return [walk(node), tab]
}

function doInsertTab(node, paneId, tab) {
  return mapLeaf(node, paneId, n => ({
    tabs: [...n.tabs, tab],
    activeTabId: tab.id
  }))
}

function doSplitWithTab(node, targetPaneId, dir, side, fromPaneId, tabId) {
  const [extracted, tab] = doExtractTab(node, fromPaneId, tabId)
  if (!tab) return node
  function splitAndInsert(n) {
    if (n.type === 'leaf') {
      if (n.paneId !== targetPaneId) return n
      const newPane = makeLeaf([tab], tab.id)
      const [a, b] = side === 'before' ? [newPane, n] : [n, newPane]
      return makeSplit(dir, a, b)
    }
    return { ...n, a: splitAndInsert(n.a), b: splitAndInsert(n.b) }
  }
  return splitAndInsert(extracted)
}

const MIN_PANE_SIZE = 40

// Trims `amount` off whatever pane(s) sit at the top/left edge along `dir`,
// leaving every other pane's own pixel size exactly as it was — 'a' is
// always the top/left, fixed-px side of a split (see makeSplit), so walking
// down through matching-dir splits' 'a' side (and both sides of any
// perpendicular split, since those don't move along `dir` at all) reaches
// exactly the pane(s) that actually need to shrink.
function shrinkTopLeftEdge(node, dir, amount) {
  if (node.type === 'leaf') return node
  if (node.dir === dir) {
    return { ...node, sizeA: Math.max(MIN_PANE_SIZE, node.sizeA - amount), a: shrinkTopLeftEdge(node.a, dir, amount) }
  }
  return { ...node, a: shrinkTopLeftEdge(node.a, dir, amount), b: shrinkTopLeftEdge(node.b, dir, amount) }
}

// Wraps the whole tree in a new root split so the fresh pane spans the full
// window edge regardless of how the existing tree is carved up — but unlike
// a plain wrap, only the pane(s) actually touching that edge shrink to make
// room; everything else keeps its exact pixel size. For the 'after' (bottom/
// right) edge this falls out for free: the old tree becomes the wrap's fixed
// 'a' side, and flex:1 already funnels all the space change to whichever
// pane sits furthest along `dir` inside it. For 'before' (top/left) the
// fresh pane itself is the fixed 'a' side, so the old tree's own top/left
// chain needs trimming explicitly (shrinkTopLeftEdge) by the same amount.
function doSplitSpanning(node, dir, side, sizeA, label, presetId) {
  const tabId = newTabId()
  const fresh = makeLeaf([{ id: tabId, label, sliders: [], groups: [] }], tabId)
  if (side === 'before') {
    return makeSplit(dir, fresh, shrinkTopLeftEdge(node, dir, sizeA), sizeA, presetId)
  }
  return makeSplit(dir, node, fresh, sizeA, presetId)
}

// ── exported mutations (all apply to the active workspace) ───────────────────
export function updatePane(paneId, fn) {
  updateActiveLayout(l => mapLeaf(l, paneId, fn))
}

export function setSplitSize(splitId, sizeA) {
  updateActiveLayout(l => mapSplit(l, splitId, () => ({ sizeA })))
}

// presetId (optional): if the caller already generated the new split's id via
// newSplitId() (e.g. to target it with setSplitSize before this update
// resolves), pass it here so the tree uses that same id instead of minting
// its own.
export function splitPane(paneId, dir, side = 'after', sizeA = 260, presetId) {
  updateActiveLayout(l => {
    const labels = allLeaves(l).flatMap(leaf => leaf.tabs.map(t => t.label))
    const label  = nextAvailableName(labels, 'Tab')
    return doSplit(l, paneId, dir, side, sizeA, label, presetId)
  })
}

export function splitPaneSpanning(dir, side = 'after', sizeA = 260, presetId) {
  updateActiveLayout(l => {
    const labels = allLeaves(l).flatMap(leaf => leaf.tabs.map(t => t.label))
    const label  = nextAvailableName(labels, 'Tab')
    return doSplitSpanning(l, dir, side, sizeA, label, presetId)
  })
}

export function collapsePane(paneId) {
  updateActiveLayout(l => {
    const result = doCollapse(l, paneId)
    // Don't collapse the last pane
    return result?.type ? result : l
  })
}

export function moveTab(fromPaneId, tabId, toPaneId) {
  if (fromPaneId === toPaneId) return
  updateActiveLayout(l => {
    const [extracted, tab] = doExtractTab(l, fromPaneId, tabId)
    if (!tab) return l
    return doInsertTab(extracted, toPaneId, tab)
  })
}

export function splitWithTab(targetPaneId, dir, side, fromPaneId, tabId) {
  updateActiveLayout(l => doSplitWithTab(l, targetPaneId, dir, side, fromPaneId, tabId))
}

// Legacy restore path: an old save had a single tree, not a workspace list —
// collapse it into the current (or a fresh) single workspace.
export function restoreLayout(newLayout) {
  reconcileCounters(newLayout)
  newLayout = migrateTabPositions(newLayout)
  const id = get(activeWorkspaceId) ?? wid()
  workspaces.set([{ id, label: 'Workspace 1', layout: newLayout }])
  activeWorkspaceId.set(id)
}

// Full restore from the new multi-workspace state format.
export function restoreWorkspaces(workspaceList, activeId) {
  for (const w of workspaceList) {
    _wc = bumpCounterPastId(w.id, 'ws_', _wc)
    reconcileCounters(w.layout)
  }
  workspaceList = workspaceList.map(w => ({ ...w, layout: migrateTabPositions(w.layout) }))
  workspaces.set(workspaceList)
  activeWorkspaceId.set(activeId ?? workspaceList[0]?.id ?? null)
}

// Full structural reset — back to a single empty workspace/pane/tab. Unlike
// clearAllWorkspaces() (empties sliders/groups, keeps the layout), this drops
// the layout itself too.
export function resetToDefault() {
  const id = wid()
  workspaces.set([{ id, label: 'Workspace 1', layout: makeLeaf() }])
  activeWorkspaceId.set(id)
}

// ── workspace management ──────────────────────────────────────────────────────
export function addWorkspace() {
  const id = wid()
  workspaces.update(list => {
    const label = nextAvailableName(list.map(w => w.label), 'Workspace')
    return [...list, { id, label, layout: makeLeaf() }]
  })
  activeWorkspaceId.set(id)
}

export function removeWorkspace(id) {
  let nextActive = null
  workspaces.update(list => {
    if (list.length <= 1) return list   // never remove the last workspace
    const filtered = list.filter(w => w.id !== id)
    nextActive = filtered[0]?.id ?? null
    return filtered
  })
  if (get(activeWorkspaceId) === id && nextActive) activeWorkspaceId.set(nextActive)
}

export function renameWorkspace(id, label) {
  workspaces.update(list => list.map(w => w.id === id ? { ...w, label } : w))
}

export function setActiveWorkspace(id) {
  activeWorkspaceId.set(id)
}

// Maps every tab, in every pane, of one layout tree through fn — used to reach
// into every workspace at once (a captured GH object's id could in principle
// show up in more than one, e.g. slider name sync or a global clear).
function mapAllTabs(node, fn) {
  if (node.type === 'leaf') return { ...node, tabs: node.tabs.map(fn) }
  return { ...node, a: mapAllTabs(node.a, fn), b: mapAllTabs(node.b, fn) }
}
function updateAllWorkspaces(fn) {
  workspaces.update(list => list.map(w => ({ ...w, layout: fn(w.layout) })))
}

// Maps every tab, in every pane, across every workspace through fn.
export function updateAllTabs(fn) {
  updateAllWorkspaces(node => mapAllTabs(node, fn))
}

// Applies a live OS window-border resize delta to every workspace's root
// extent (not just the active one — a physical window resize changes every
// workspace's available space, unlike a manual divider drag which is scoped
// to the visible layout via updateActiveLayout). Reuses shrinkTopLeftEdge
// (written for the spanning-split feature) unchanged: deltaX/deltaY are the
// window's Location delta since the last tick, which is only nonzero on the
// side whose top/left edge actually moved — the opposite (bottom/right) edge
// case needs no tree mutation at all, flex:1 on the 'b' side already absorbs
// it. Per-axis guards avoid rebuilding every workspace's tree on a no-op tick.
export function applyWindowEdgeResize(deltaX, deltaY) {
  if (!deltaX && !deltaY) return
  updateAllWorkspaces(node => {
    let n = node
    if (deltaX) n = shrinkTopLeftEdge(n, 'h', deltaX)
    if (deltaY) n = shrinkTopLeftEdge(n, 'v', deltaY)
    return n
  })
}

// Clears sliders/groups from every tab, across ALL workspaces — mirrors the
// C# side's ClearAll(), which drops every tracked GH object regardless of
// which workspace it happened to be captured into.
export function clearAllWorkspaces() {
  updateAllTabs(t => ({ ...t, sliders: [], groups: [] }))
}

// ── targeted control sync (name / panel text / item-picker options) ──────────
// C# pushes these on every GH document solve — for every tracked slider,
// panel, item picker, across every workspace, whether or not it's the one
// currently visible. A blind updateAllTabs() rebuild would reconstruct every
// workspace's whole tree for every single push. syncControl instead skips any
// workspace/pane/tab/group that doesn't actually contain the id, so unrelated
// branches keep their exact object reference — no wasted work, and Svelte
// doesn't even consider re-rendering what didn't change.
function patchGroupsIfPresent(groups, id, patch) {
  let changed = false
  const next = groups.map(g => {
    const hasHere = g.sliders.some(s => s.id === id)
    const [subGroups, subChanged] = patchGroupsIfPresent(g.groups ?? [], id, patch)
    if (!hasHere && !subChanged) return g
    changed = true
    return {
      ...g,
      sliders: hasHere ? g.sliders.map(s => s.id === id ? { ...s, ...patch } : s) : g.sliders,
      groups: subGroups
    }
  })
  return [changed ? next : groups, changed]
}

function patchNodeIfPresent(node, id, patch) {
  if (node.type === 'leaf') {
    let changed = false
    const tabs = node.tabs.map(t => {
      const hasHere = t.sliders.some(s => s.id === id)
      const [groups, subChanged] = patchGroupsIfPresent(t.groups, id, patch)
      if (!hasHere && !subChanged) return t
      changed = true
      return {
        ...t,
        sliders: hasHere ? t.sliders.map(s => s.id === id ? { ...s, ...patch } : s) : t.sliders,
        groups
      }
    })
    return changed ? { ...node, tabs } : node
  }
  const a = patchNodeIfPresent(node.a, id, patch)
  const b = patchNodeIfPresent(node.b, id, patch)
  return (a === node.a && b === node.b) ? node : { ...node, a, b }
}

export function syncControl(id, patch) {
  workspaces.update(list => list.map(w => {
    const layout = patchNodeIfPresent(w.layout, id, patch)
    return layout === w.layout ? w : { ...w, layout }
  }))
}

// Find the splitId of the immediate parent split that contains paneId as a direct leaf child
export function findParentSplitId(node, paneId) {
  if (node.type === 'leaf') return null
  if ((node.a.type === 'leaf' && node.a.paneId === paneId) ||
      (node.b.type === 'leaf' && node.b.paneId === paneId)) return node.splitId
  return findParentSplitId(node.a, paneId) ?? findParentSplitId(node.b, paneId)
}

function leftmostLeafId(node) { return node.type === 'leaf' ? node.paneId : leftmostLeafId(node.a) }
function rightmostLeafId(node) { return node.type === 'leaf' ? node.paneId : rightmostLeafId(node.b) }

// Find paneId of the adjacent pane in the given direction from paneId.
//
// Crossing the nearest matching-direction split only tells you which SIDE
// (a/b subtree) the neighbor is in — in a 2x2 (or deeper) grid that side is
// itself split further, and grabbing its flat leftmost/rightmost leaf picks
// whichever leaf happens to be first in that subtree, regardless of row/
// column. E.g. bottom-left's "after"/horizontal neighbor in a 2x2 grid used
// to resolve to top-right (leftmostLeafId of the whole right side) instead
// of bottom-right (the pane actually adjacent to it).
//
// Fix: after crossing into the sibling subtree, replay the same a/b choices
// the source leaf's path made at every split AFTER the crossing point (i.e.
// every split along a different axis than the one we just crossed) — that
// walks down to the leaf that shares the source's position on those other
// axes, which is the one that's actually geometrically adjacent.
export function findNeighborPane(node, paneId, dir, side) {
  function descend(n, remainingPath) {
    for (const { which } of remainingPath) {
      if (n.type === 'leaf') break
      n = n[which]
    }
    return n.type === 'leaf' ? n.paneId : leftmostLeafId(n)
  }
  function walk(n, path) {
    if (n.type === 'leaf') {
      if (n.paneId !== paneId) return null
      for (let i = path.length - 1; i >= 0; i--) {
        const { split, which } = path[i]
        if (split.dir !== dir) continue
        if (side === 'after'  && which === 'a') return descend(split.b, path.slice(i + 1))
        if (side === 'before' && which === 'b') return descend(split.a, path.slice(i + 1))
      }
      return null
    }
    return walk(n.a, [...path, { split: n, which: 'a' }])
        ?? walk(n.b, [...path, { split: n, which: 'b' }])
  }
  return walk(node, [])
}

// ── unified slider/group ordering ─────────────────────────────────────────────
// Sliders and groups live in two separate arrays (sliders[]/groups[]) — kept
// that way because the C# side (SlateWindow.cs's RestoreState) reads exactly
// those two keys off the saved JSON to reattach live GH_NumberSlider refs, so
// the wire shape can't change. Visual order instead comes from a `pos` field
// on each item; these helpers are the single place that combines the two
// arrays into one ordered view, used by both rendering (Pane.svelte,
// GroupSection.svelte) and every insert/reorder function below.
export function orderedItems(container) {
  const items = [
    ...container.sliders.map(s => ({ kind: 'slider', id: s.id, pos: s.pos ?? 0, data: s })),
    ...(container.groups ?? []).map(g => ({ kind: 'group', id: g.id, pos: g.pos ?? 0, data: g })),
  ]
  items.sort((a, b) => a.pos - b.pos)
  return items
}

// Fractional position strictly between two neighbors — either can be null
// ("no neighbor on that side"). Inserting between two existing items this way
// never requires renumbering anything else in the container.
export function posBetween(before, after) {
  if (before == null && after == null) return 0
  if (before == null) return after - 1
  if (after == null)  return before + 1
  return (before + after) / 2
}

// Position for appending after everything currently in a container.
export function posAppend(container) {
  const items = orderedItems(container)
  return items.length ? items[items.length - 1].pos + 1 : 0
}

// Assigns each of `items` a fresh, sequentially-increasing pos starting right
// after whatever's already in `container` — used by every "just tack this
// onto the end" mutation (cross-pane/cross-tab/cross-group moves, capture,
// group-removal promoting its children back to the parent).
export function withAppendedPositions(container, items) {
  let p = posAppend(container)
  return items.map(it => ({ ...it, pos: p++ }))
}

// Old saves have no `pos` field — assigns one from today's fixed visual order
// (all sliders, then all groups, recursively into each nested group) so a
// restored file looks exactly like it did before `pos` existed. A no-op for
// any item that already has one (lets a partially-migrated tree pass through
// unchanged too).
function migratePositions(container) {
  let i = 0
  const sliders = container.sliders.map(s => s.pos != null ? s : { ...s, pos: i++ })
  const groups = (container.groups ?? []).map(g => {
    const g2 = g.pos != null ? g : { ...g, pos: i++ }
    return { ...g2, ...migratePositions(g2) }
  })
  return { sliders, groups }
}
function migrateTabPositions(node) {
  if (node.type === 'leaf') return { ...node, tabs: node.tabs.map(t => ({ ...t, ...migratePositions(t) })) }
  return { ...node, a: migrateTabPositions(node.a), b: migrateTabPositions(node.b) }
}

// Pulls every slider whose id is in idSet out of a tab (top-level + any depth of
// nested groups), preserving each list's relative order. Exported so Pane.svelte's
// own multi-select drag/move logic can reuse it instead of keeping a second copy.
export function extractSlidersByIds(tab, idSet) {
  const extracted = []
  function stripSliders(sliders) {
    return sliders.filter(s => {
      if (idSet.has(s.id)) { extracted.push(s); return false }
      return true
    })
  }
  function stripGroups(groups) {
    return groups.map(g => ({ ...g, sliders: stripSliders(g.sliders), groups: stripGroups(g.groups ?? []) }))
  }
  const sliders = stripSliders(tab.sliders)
  const groups  = stripGroups(tab.groups)
  return [{ ...tab, sliders, groups }, extracted]
}

// Move a slider (or a whole multi-selected block of sliders) or a single group
// from one pane/tab to another pane/tab
export function moveCrossPaneItem(fromPaneId, fromTabId, itemIds, itemType, toPaneId, toTabId) {
  updateActiveLayout(l => {
    let items = []
    function extract(n) {
      if (n.type === 'split') return { ...n, a: extract(n.a), b: extract(n.b) }
      if (n.paneId !== fromPaneId) return n
      return { ...n, tabs: n.tabs.map(t => {
        if (t.id !== fromTabId) return t
        if (itemType === 'slider') {
          const [stripped, extracted] = extractSlidersByIds(t, new Set(itemIds))
          items = extracted
          return stripped
        } else {
          const found = t.groups.find(g => g.id === itemIds[0])
          items = found ? [found] : []
          return { ...t, groups: t.groups.filter(g => g.id !== itemIds[0]) }
        }
      })}
    }
    function insert(n) {
      if (n.type === 'split') return { ...n, a: insert(n.a), b: insert(n.b) }
      if (n.paneId !== toPaneId) return n
      return { ...n, tabs: n.tabs.map(t => {
        if (t.id !== toTabId) return t
        const placed = withAppendedPositions(t, items)
        if (itemType === 'slider') return { ...t, sliders: [...t.sliders, ...placed] }
        return { ...t, groups: [...t.groups, ...placed] }
      })}
    }
    const extracted = extract(l)
    if (!items.length) return l
    return insert(extracted)
  })
}
