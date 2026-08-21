import { writable } from 'svelte/store'

// ── ID generators ─────────────────────────────────────────────────────────────
let _pc = 0, _sc = 0, _tc = 0
const pid  = () => 'pane_'  + (++_pc)
const sid  = () => 'split_' + (++_sc)
export const newTabId = () => 'tab_' + Date.now() + '_' + (++_tc)

// ── constructors ──────────────────────────────────────────────────────────────
export function makeLeaf(tabs, activeTabId) {
  if (!tabs) {
    const id = newTabId()
    tabs = [{ id, label: 'Main', sliders: [], groups: [] }]
    activeTabId = id
  }
  return { type: 'leaf', paneId: pid(), tabs, activeTabId: activeTabId ?? tabs[0]?.id ?? null }
}

function makeSplit(dir, a, b, ratio = 0.5) {
  return { type: 'split', splitId: sid(), dir, ratio, a, b }
}

// ── store ─────────────────────────────────────────────────────────────────────
export const layout = writable(makeLeaf())

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

function doSplit(node, paneId, dir, side, ratio) {
  if (node.type === 'leaf') {
    if (node.paneId !== paneId) return node
    const fresh = makeLeaf([], null)
    const [a, b] = side === 'before' ? [fresh, node] : [node, fresh]
    return makeSplit(dir, a, b, ratio)
  }
  return { ...node, a: doSplit(node.a, paneId, dir, side, ratio), b: doSplit(node.b, paneId, dir, side, ratio) }
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

// ── exported mutations ────────────────────────────────────────────────────────
export function updatePane(paneId, fn) {
  layout.update(l => mapLeaf(l, paneId, fn))
}

export function setRatio(splitId, ratio) {
  layout.update(l => mapSplit(l, splitId, () => ({ ratio })))
}

export function splitPane(paneId, dir, side = 'after', ratio = 0.5) {
  layout.update(l => doSplit(l, paneId, dir, side, ratio))
}

export function collapsePane(paneId) {
  layout.update(l => {
    const result = doCollapse(l, paneId)
    // Don't collapse the last pane
    return result?.type ? result : l
  })
}

export function moveTab(fromPaneId, tabId, toPaneId) {
  if (fromPaneId === toPaneId) return
  layout.update(l => {
    const [extracted, tab] = doExtractTab(l, fromPaneId, tabId)
    if (!tab) return l
    return doInsertTab(extracted, toPaneId, tab)
  })
}

export function splitWithTab(targetPaneId, dir, side, fromPaneId, tabId) {
  layout.update(l => doSplitWithTab(l, targetPaneId, dir, side, fromPaneId, tabId))
}

// Restore entire layout (from C# state)
export function restoreLayout(newLayout) {
  layout.set(newLayout)
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

// Find paneId of the adjacent pane in the given direction from paneId
export function findNeighborPane(node, paneId, dir, side) {
  function walk(n, path) {
    if (n.type === 'leaf') {
      if (n.paneId !== paneId) return null
      for (let i = path.length - 1; i >= 0; i--) {
        const { split, which } = path[i]
        if (split.dir !== dir) continue
        if (side === 'after'  && which === 'a') return leftmostLeafId(split.b)
        if (side === 'before' && which === 'b') return rightmostLeafId(split.a)
      }
      return null
    }
    return walk(n.a, [...path, { split: n, which: 'a' }])
        ?? walk(n.b, [...path, { split: n, which: 'b' }])
  }
  return walk(node, [])
}

// Move a slider or group from one pane/tab to another pane/tab
export function moveCrossPaneItem(fromPaneId, fromTabId, fromGroupId, itemId, itemType, toPaneId, toTabId) {
  layout.update(l => {
    let item = null
    function extract(n) {
      if (n.type === 'split') return { ...n, a: extract(n.a), b: extract(n.b) }
      if (n.paneId !== fromPaneId) return n
      return { ...n, tabs: n.tabs.map(t => {
        if (t.id !== fromTabId) return t
        if (itemType === 'slider') {
          if (fromGroupId) {
            return { ...t, groups: t.groups.map(g => {
              if (g.id !== fromGroupId) return g
              item = g.sliders.find(s => s.id === itemId)
              return { ...g, sliders: g.sliders.filter(s => s.id !== itemId) }
            })}
          } else {
            item = t.sliders.find(s => s.id === itemId)
            return { ...t, sliders: t.sliders.filter(s => s.id !== itemId) }
          }
        } else {
          item = t.groups.find(g => g.id === itemId)
          return { ...t, groups: t.groups.filter(g => g.id !== itemId) }
        }
      })}
    }
    function insert(n) {
      if (n.type === 'split') return { ...n, a: insert(n.a), b: insert(n.b) }
      if (n.paneId !== toPaneId) return n
      return { ...n, tabs: n.tabs.map(t => {
        if (t.id !== toTabId) return t
        if (itemType === 'slider') return { ...t, sliders: [...t.sliders, item] }
        return { ...t, groups: [...t.groups, item] }
      })}
    }
    const extracted = extract(l)
    if (!item) return l
    return insert(extracted)
  })
}
