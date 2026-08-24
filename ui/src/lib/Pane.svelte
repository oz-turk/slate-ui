<script>
  import TabBar       from './TabBar.svelte'
  import SliderRow    from './SliderRow.svelte'
  import ToggleRow    from './ToggleRow.svelte'
  import ButtonRow    from './ButtonRow.svelte'
  import ValueListRow from './ValueListRow.svelte'
  import PanelRow     from './PanelRow.svelte'
  import ColourPickerRow from './ColourPickerRow.svelte'
  import GroupSection from './GroupSection.svelte'
  import ActionBar    from './ActionBar.svelte'
  import CornerHandle from './CornerHandle.svelte'
  import ContextMenu  from './ContextMenu.svelte'
  import { layout, updatePane, findLeaf, newTabId, splitPane, newSplitId, setSplitSize, collapsePane, findNeighborPane, moveCrossPaneItem, extractSlidersByIds } from '../stores/layout.js'
  import { tabDrag, itemDrag, collapsePreview } from '../stores/dragState.js'
  import { computeAlignedSnapTargets, snapRaw } from './splitSnap.js'
  import { mode, deleteRequest, captureRequest, clearSelectionTick, hoverHint, theme } from '../stores/uiState.js'
  import { postToCs, postStateSnapshot } from './ipc.js'
  import { flip } from 'svelte/animate'
  import { cubicOut } from 'svelte/easing'

  export let paneId

  // slider.type → row component (falls back to SliderRow when unset/unknown)
  // itemPicker reuses ValueListRow — same "pick one from a list" UI, the only
  // difference (candidate list from a wired input vs manually authored) lives
  // entirely on the C# side.
  const ROW_COMPONENTS = { toggle: ToggleRow, button: ButtonRow, valueList: ValueListRow, panel: PanelRow, itemPicker: ValueListRow, humanValueList: ValueListRow, colourPicker: ColourPickerRow, pancakeButton: ButtonRow }

  // ── derive pane state reactively (not derived() — paneId must stay live) ─────
  $: pane        = findLeaf($layout, paneId)
  $: tabs        = pane?.tabs        ?? []
  $: activeTabId = pane?.activeTabId ?? null
  $: activeTab   = tabs.find(t => t.id === activeTabId) ?? tabs[0]

  // SliderRow's name column is a fixed grid width (needs to line up across
  // every row so the tracks align) — sized here per-tab from that tab's own
  // longest slider name (top-level + nested in groups), rather than one
  // constant for every tab, so "Slider1" doesn't leave a wide dead gap while
  // a longer name elsewhere still fits. Rough char-count estimate, not a
  // measured width — biased wide (not tight) since a little extra slack costs
  // nothing but an under-estimate clips into the ellipsis.
  function isSliderRowType(s) { return !ROW_COMPONENTS[s.type] }
  function maxSliderNameLen(sliders, groups) {
    let max = 0
    for (const s of sliders ?? []) if (isSliderRowType(s)) max = Math.max(max, (s.name ?? '').length)
    for (const g of groups ?? []) max = Math.max(max, maxSliderNameLen(g.sliders, g.groups))
    return max
  }
  $: nameColWidth = Math.min(180, Math.max(70, maxSliderNameLen(activeTab?.sliders, activeTab?.groups) * 7 + 20))

  // Driven straight from the theme store instead of the --edge-tint CSS
  // custom property — that var resolves fine for every *other* consumer
  // (--ws-tab-bg etc, same override block) but this specific border kept
  // rendering solid black in light mode across several rebuilds, so this
  // sidesteps whatever cascade issue that was rather than keep guessing at it.
  $: edgeTint   = $theme === 'light' ? 'rgba(0, 0, 0, 0.08)'   : 'rgba(255, 255, 255, 0.04)'
  $: dangerTint = $theme === 'light' ? 'rgba(194, 59, 59, 0.6)' : 'rgba(255, 59, 59, 0.6)'
  // Collapse intent overlay used to be border-only — a 2px line is easy to
  // lose against overflow:hidden + border-radius clipping at the pane edges
  // (the one working analogue, .drop-overlay.zone-center, always pairs its
  // border with a background fill). Adding the same fill here so there's a
  // large painted area, not just a thin outline, to catch.
  $: dangerFill = $theme === 'light' ? 'rgba(194, 59, 59, 0.16)' : 'rgba(255, 59, 59, 0.14)'

  // "x"/"c" hotkeys — App.svelte resolves what's under the mouse via the DOM
  // (data-pane-id / data-slider-id) and sets these; whichever Pane matches acts.
  $: if ($deleteRequest?.paneId === paneId && activeTab) {
    removeSlider(activeTab.id, $deleteRequest.sliderId)
    deleteRequest.set(null)
  }
  $: if ($captureRequest === paneId && activeTabId) {
    postToCs({ type: 'capture', tabId: activeTabId })
    captureRequest.set(null)
  }

  // Escape — every pane clears its own selection, not just the one under the mouse.
  $: if ($clearSelectionTick) clearSelection()

  // Leaving edit mode: selection is an edit-mode concept, so the blue
  // highlight shouldn't persist once preview mode hides the ActionBar.
  $: if ($mode !== 'edit') clearSelection()

  // ── local UI state ────────────────────────────────────────────────────────────
  let selectedIds   = new Set()
  let activeDrag    = null
  let dropTarget    = null
  let lastReorderKey = null  // dedupe key so hovering the same slider-row spot doesn't re-splice every event
  let reorderRaf      = null // pending rAF for the deferred live reorder (see setDropTarget)

  // ── tab drag (cross-pane) drop state ─────────────────────────────────────────
  let edgeZone     = null   // 'left' | 'right' | 'top' | 'bottom' | 'center' | null
  let itemDragHover = false  // true only while a cross-pane item drag is hovering this pane
  let paneEl
  let resizingSliderId = null  // row currently being resized — flip is skipped for it (see row-slot)
  let contextMenu = null  // { x, y, items } | null — right-click menu on the pane itself

  function onPaneContextMenu(e) {
    e.preventDefault()
    const menuW = 170, menuH = 130
    contextMenu = {
      x: Math.min(e.clientX, window.innerWidth  - menuW - 8),
      y: Math.min(e.clientY, window.innerHeight - menuH - 8),
      items: [
        { label: 'Split Horizontally', action: () => { splitPane(paneId, 'h', 'after'); postStateSnapshot() } },
        { label: 'Split Vertically',   action: () => { splitPane(paneId, 'v', 'after'); postStateSnapshot() } },
        'sep',
        { label: 'Close Pane', danger: true, action: () => { collapsePane(paneId); postStateSnapshot() } },
      ]
    }
  }

  // ── pane mutation helpers ─────────────────────────────────────────────────────
  function mutateTabs(fn) {
    updatePane(paneId, p => {
      const tabs = fn([...p.tabs])
      return { tabs }
    })
    postStateSnapshot()
  }

  // same as mutateTabs but skips the snapshot post — used for live drag reorder,
  // which fires on every hover change; snapshot is sent once when the drag ends
  function mutateTabsQuiet(fn) {
    updatePane(paneId, p => ({ tabs: fn([...p.tabs]) }))
  }

  function setActive(id) {
    updatePane(paneId, () => ({ activeTabId: id }))
  }

  // ── group tree helpers ────────────────────────────────────────────────────────
  function mapGroupTree(groups, groupId, fn) {
    return groups.map(g =>
      g.id === groupId
        ? { ...g, ...fn(g) }
        : { ...g, groups: mapGroupTree(g.groups ?? [], groupId, fn) }
    )
  }
  function removeFromGroupTree(groups, groupId) {
    return groups
      .filter(g => g.id !== groupId)
      .map(g => ({ ...g, groups: removeFromGroupTree(g.groups ?? [], groupId) }))
  }
  function extractGroupFromTree(groups, groupId) {
    let found = null
    function walk(gs) {
      return gs.filter(g => { if (g.id === groupId) { found = g; return false } return true })
               .map(g => ({ ...g, groups: walk(g.groups ?? []) }))
    }
    return [walk(groups), found]
  }
  // Inserts a block of sliders together, next to targetId, in whichever list
  // (top-level or targetGroupId's) it lives in — order within the block is kept.
  function insertSlidersAt(tab, targetGroupId, targetId, pos, items) {
    if (targetGroupId) {
      return { ...tab, groups: mapGroupTree(tab.groups, targetGroupId, g => {
        const sliders = [...g.sliders]
        const toIdx = sliders.findIndex(s => s.id === targetId)
        sliders.splice(toIdx < 0 ? sliders.length : pos === 'after' ? toIdx + 1 : toIdx, 0, ...items)
        return { sliders }
      })}
    }
    const sliders = [...tab.sliders]
    const toIdx = sliders.findIndex(s => s.id === targetId)
    sliders.splice(toIdx < 0 ? sliders.length : pos === 'after' ? toIdx + 1 : toIdx, 0, ...items)
    return { ...tab, sliders }
  }

  // value may be a plain value or a (prevValue => newValue) updater — the
  // latter lets callers (e.g. checklist toggles) compute the new value from
  // whatever it currently is, without a separate read first.
  function updateSliderInGroups(groups, sliderId, value) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.map(s => s.id === sliderId ? { ...s, value: typeof value === 'function' ? value(s.value) : value } : s),
      groups: updateSliderInGroups(g.groups ?? [], sliderId, value)
    }))
  }
  function removeSliderFromGroups(groups, sliderId) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.filter(s => s.id !== sliderId),
      groups: removeSliderFromGroups(g.groups ?? [], sliderId)
    }))
  }

  // ── slider value update (no snapshot — too frequent) ─────────────────────────
  function updateSliderValue(sliderId, value) {
    updatePane(paneId, p => ({
      tabs: p.tabs.map(t => ({
        ...t,
        sliders: t.sliders.map(s => s.id === sliderId ? { ...s, value: typeof value === 'function' ? value(s.value) : value } : s),
        groups:  updateSliderInGroups(t.groups, sliderId, value)
      }))
    }))
  }

  // For a multiSelect control (CheckList/Sequence), `value` is the index the
  // user just clicked — toggle it into/out of the current selection locally,
  // but tell C# just that one index; ToggleItem() there does the rest.
  function changeMessageType(controlType) {
    if (controlType === 'panel') return 'panel_change'
    if (controlType === 'colourPicker') return 'colour_change'
    return 'slider_change'
  }
  function onSliderChange(sliderId, value, controlType, multiSelect) {
    if (multiSelect) {
      updateSliderValue(sliderId, prev => {
        const arr = Array.isArray(prev) ? prev : []
        return arr.includes(value) ? arr.filter(i => i !== value) : [...arr, value].sort((a, b) => a - b)
      })
    } else {
      updateSliderValue(sliderId, value)
    }
    postToCs({ type: changeMessageType(controlType), id: sliderId, value })
  }
  function onSliderCommit(sliderId, value, controlType) {
    updateSliderValue(sliderId, value)
    postToCs({ type: changeMessageType(controlType), id: sliderId, value })
  }

  // ── panel resize (UI-only — no postToCs, GH doesn't know about row height) ───
  function patchSliderFieldInGroups(groups, sliderId, patch) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.map(s => s.id === sliderId ? { ...s, ...patch } : s),
      groups: patchSliderFieldInGroups(g.groups ?? [], sliderId, patch)
    }))
  }
  function onPanelResize(sliderId, height, commit) {
    updatePane(paneId, p => ({
      tabs: p.tabs.map(t => ({
        ...t,
        sliders: t.sliders.map(s => s.id === sliderId ? { ...s, height } : s),
        groups:  patchSliderFieldInGroups(t.groups, sliderId, { height })
      }))
    }))
    if (commit) postStateSnapshot()
  }

  // ── tab management ────────────────────────────────────────────────────────────
  function addTab() {
    const id = newTabId()
    mutateTabs(tabs => [...tabs, { id, label: 'Tab ' + (tabs.length + 1), sliders: [], groups: [] }])
    updatePane(paneId, () => ({ activeTabId: id }))
  }

  function renameTab(id, label) {
    mutateTabs(tabs => tabs.map(t => t.id === id ? { ...t, label } : t))
  }

  function setTabColor(id, color) {
    mutateTabs(tabs => tabs.map(t => t.id === id ? { ...t, color } : t))
  }

  function removeTab(id) {
    updatePane(paneId, p => {
      if (p.tabs.length <= 1) return {}
      const next = p.tabs.find(t => t.id !== id)
      return {
        tabs: p.tabs.filter(t => t.id !== id),
        activeTabId: p.activeTabId === id ? next?.id : p.activeTabId
      }
    })
    postStateSnapshot()
  }

  // ── slider & group drag-drop (within pane) ────────────────────────────────────
  function startDrag(type, id, fromTabId, fromGroupId = null) {
    // If the grabbed row is part of the current multi-selection, drag the whole
    // selection together — otherwise just this one item.
    const ids = (type === 'slider' && selectedIds.has(id) && selectedIds.size > 1)
      ? [...selectedIds]
      : [id]
    activeDrag = { type, id, ids, fromTabId, fromGroupId }
    itemDrag.set({ type, id, ids, fromPaneId: paneId, fromTabId, fromGroupId })
  }
  function endDrag() {
    activeDrag = null; dropTarget = null; itemDrag.set(null); lastReorderKey = null
    if (reorderRaf) { cancelAnimationFrame(reorderRaf); reorderRaf = null }
  }
  function setDropTarget(type, id, pos = null, groupId = null) {
    if (!activeDrag) return
    dropTarget = { type, id, pos, groupId }
    if (type === 'slider-row' && activeDrag.type === 'slider') {
      const key = `${id}:${pos}:${groupId}`
      if (key !== lastReorderKey) {
        lastReorderKey = key
        // Defer the DOM-reordering mutation off the dragover event itself —
        // moving the hovered node mid-event confuses the browser's native
        // drag feedback and shows a "not-allowed" cursor. Doing it a frame
        // later lets the browser finish registering preventDefault() first.
        if (reorderRaf) cancelAnimationFrame(reorderRaf)
        reorderRaf = requestAnimationFrame(() => {
          reorderRaf = null
          if (activeDrag) liveReorderSlider(id, groupId, pos)
        })
      }
    }
  }
  function clearDropTarget(type, id) {
    if (dropTarget?.type === type && dropTarget.id === id) dropTarget = null
  }

  function executeDrop(type, id, pos) {
    if (!activeDrag) return
    const { type: dt, id: di, ids: dragIds, fromTabId } = activeDrag
    if (type === 'tab') {
      if (fromTabId === id) { endDrag(); return }
      if (dt === 'slider') moveSlidersToTab(dragIds, fromTabId, id)
      else                 moveGroupToTab(di, fromTabId, id)
    } else if (type === 'group-header') {
      if (dt === 'slider') moveSlidersToGroup(dragIds, fromTabId, id)
      else if (dt === 'group') nestGroupInGroup(di, fromTabId, id)
    }
    // slider-row reordering already happened live during dragover — drop just finalizes
    endDrag()
    postStateSnapshot()
  }

  function withTab(tabId, fn) {
    mutateTabs(tabs => tabs.map(t => t.id === tabId ? fn(t) : t))
  }

  function moveSlidersToTab(sliderIds, fromTabId, toTabId) {
    const idSet = new Set(sliderIds)
    let moved = []
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        const [stripped, items] = extractSlidersByIds(t, idSet)
        moved = items
        return stripped
      })
      if (!moved.length) return tabs
      return t1.map(t => t.id === toTabId ? { ...t, sliders: [...t.sliders, ...moved] } : t)
    })
  }

  function moveGroupToTab(groupId, fromTabId, toTabId) {
    let group = null
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        group = t.groups.find(g => g.id === groupId)
        return { ...t, groups: t.groups.filter(g => g.id !== groupId) }
      })
      if (!group) return tabs
      return t1.map(t => t.id === toTabId ? { ...t, groups: [...t.groups, group] } : t)
    })
  }

  // Reorders live, on every dragover hover change (not just on drop), so siblings
  // visibly shift out of the way as the drag progresses. Moves the whole dragged
  // block (activeDrag.ids — more than one when dragging a multi-selection) together,
  // wherever each member currently lives, into targetId's list (top-level or
  // targetGroupId's), preserving the block's relative order.
  function liveReorderSlider(targetId, targetGroupId, pos) {
    if (!activeDrag) return
    const dragIds = new Set(activeDrag.ids ?? [activeDrag.id])
    if (dragIds.has(targetId)) return

    mutateTabsQuiet(tabs => tabs.map(t => {
      if (t.id !== activeDrag.fromTabId) return t
      const [stripped, items] = extractSlidersByIds(t, dragIds)
      if (!items.length) return t
      return insertSlidersAt(stripped, targetGroupId, targetId, pos, items)
    }))

    activeDrag = { ...activeDrag, fromGroupId: targetGroupId }
  }

  function moveSlidersToGroup(sliderIds, fromTabId, targetGroupId) {
    const idSet = new Set(sliderIds)
    let moved = []
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        const [stripped, items] = extractSlidersByIds(t, idSet)
        moved = items
        return stripped
      })
      if (!moved.length) return tabs
      return t1.map(t => t.id !== fromTabId ? t : {
        ...t,
        groups: mapGroupTree(t.groups, targetGroupId, g => ({ sliders: [...g.sliders, ...moved] }))
      })
    })
  }

  function nestGroupInGroup(sourceGroupId, fromTabId, targetGroupId) {
    if (sourceGroupId === targetGroupId) return
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== fromTabId) return t
      const [withoutSource, source] = extractGroupFromTree(t.groups, sourceGroupId)
      if (!source) return t
      return {
        ...t,
        groups: mapGroupTree(withoutSource, targetGroupId, g => ({
          groups: [...(g.groups ?? []), source]
        }))
      }
    }))
  }

  // ── selection ─────────────────────────────────────────────────────────────────
  function onSliderSelect(sliderId, multi) {
    if ($mode !== 'edit') return
    const next = new Set(selectedIds)
    if (multi) { if (next.has(sliderId)) next.delete(sliderId); else next.add(sliderId) }
    else        { selectedIds = next.has(sliderId) && next.size === 1 ? new Set() : new Set([sliderId]); return }
    selectedIds = next
  }
  function clearSelection() { selectedIds = new Set() }

  // ── ActionBar ─────────────────────────────────────────────────────────────────
  function moveSelectedTo(targetTabId) {
    if (!selectedIds.size) return
    moveSlidersToTab([...selectedIds], activeTab.id, targetTabId)
    selectedIds = new Set()
  }

  function groupSelected() {
    if (!selectedIds.size) return
    const idSet = selectedIds
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== activeTab.id) return t
      const [stripped, items] = extractSlidersByIds(t, idSet)
      const newGroup = { id: 'g_' + Date.now(), label: 'Group', collapsed: false, sliders: items, groups: [] }
      return { ...stripped, groups: [...stripped.groups, newGroup] }
    }))
    selectedIds = new Set()
  }

  // ── group helpers (recursive for nested groups) ───────────────────────────────
  function toggleGroup(groupId) {
    mutateTabs(tabs => tabs.map(t => ({
      ...t,
      groups: mapGroupTree(t.groups, groupId, g => ({ collapsed: !g.collapsed }))
    })))
  }
  function renameGroup(groupId, label) {
    mutateTabs(tabs => tabs.map(t => ({
      ...t,
      groups: mapGroupTree(t.groups, groupId, () => ({ label }))
    })))
  }
  function removeGroup(groupId) {
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== activeTab.id) return t
      // Top-level: promote sliders + sub-groups to tab
      const topIdx = t.groups.findIndex(g => g.id === groupId)
      if (topIdx >= 0) {
        const rem = t.groups[topIdx]
        return {
          ...t,
          sliders: [...t.sliders, ...(rem.sliders ?? [])],
          groups:  [...t.groups.filter(g => g.id !== groupId), ...(rem.groups ?? [])]
        }
      }
      // Nested: just remove (no promotion)
      return { ...t, groups: removeFromGroupTree(t.groups, groupId) }
    }))
  }

  // ── slider/group removal ──────────────────────────────────────────────────────
  function removeSlider(tabId, sliderId) {
    mutateTabs(tabs => tabs.map(t =>
      t.id !== tabId ? t : {
        ...t,
        sliders: t.sliders.filter(s => s.id !== sliderId),
        groups:  removeSliderFromGroups(t.groups, sliderId)
      }
    ))
    const next = new Set(selectedIds); next.delete(sliderId); selectedIds = next
  }

  // ── corner drag (split / collapse) ───────────────────────────────────────────
  // Split creates the pane immediately on the first confirmed intent
  // (CornerHandle already applies its own 8px lock threshold before it ever
  // dispatches 'split', so this doesn't need a separate one), then live-resizes
  // it for the rest of the drag — instead of the old preview-overlay-then-
  // commit-on-release flow, which felt laggy/indirect.
  //
  // Window-level listeners because splitPane() turns this pane's leaf node
  // into a split (this whole Pane instance + its CornerHandles get destroyed
  // and replaced), which drops whatever pointer capture CornerHandle was
  // holding — the eventual pointerup would otherwise never reach us. An
  // earlier version of this feature called splitPane() on every preview tick
  // instead of once and hit exactly this problem (see "Corner drag tek akış"
  // in Çözülen Problemler) — calling it once, then handing off to window
  // listeners, avoids it.
  // Guards the 'split' branch below against firing twice for one drag.
  // It's supposed to fire exactly once because splitPane() turns this
  // pane's leaf into a split (the leaf's own node type flips straight from
  // 'leaf' to 'split' at this exact tree position), which should destroy
  // this Pane instance and its CornerHandles before another pointermove
  // can arrive.
  let splitInFlight = false

  function onCornerPreview(detail) {
    if (!detail) {
      collapsePreview.set(null)
      hoverHint.set(null)
      return
    }
    if (detail.kind === 'collapse') {
      const neighborId = findNeighborPane($layout, paneId, detail.dir, detail.side)
      collapsePreview.set(neighborId ?? null)
      hoverHint.set(neighborId ? 'Release to collapse the neighbouring pane' : null)
      return
    }

    // kind === 'split' — must fire exactly once per drag (see splitInFlight).
    if (splitInFlight) return
    splitInFlight = true

    const rect = paneEl.getBoundingClientRect()
    const dim  = detail.dir === 'h' ? rect.width : rect.height
    const myOrigin = detail.dir === 'h' ? rect.left : rect.top
    const newId = newSplitId()

    // Same unconditional center + aligned-edge snap SplitDivider's own drag
    // uses (splitSnap.js) — applied from the very first frame so a freshly
    // created split snaps immediately instead of only once you grab its
    // divider afterward. The new divider doesn't exist yet on the very first
    // call (before splitPane() below), so it just finds nothing to exclude.
    let lastSnapLabel = ''
    const ratioAt = (clientX, clientY) => {
      const raw0 = detail.dir === 'h' ? (clientX - rect.left) : (clientY - rect.top)
      const excludeEl = document.querySelector(`.divider.dir-${detail.dir}[data-split-id="${newId}"]`)
      const targets = computeAlignedSnapTargets(detail.dir, myOrigin, excludeEl)
      const { raw, label } = snapRaw(raw0, dim, targets)
      lastSnapLabel = label
      return clampRatio(raw / dim)
    }

    splitPane(paneId, detail.dir, detail.side, ratioAt(detail.clientX, detail.clientY) * dim, newId)
    hoverHint.set(`Split ${detail.dir === 'h' ? 'horizontally' : 'vertically'} — drag to resize`)

    function onWindowMove(e) {
      const size = ratioAt(e.clientX, e.clientY) * dim
      setSplitSize(newId, size)
      hoverHint.set(`${Math.round(size)}px${lastSnapLabel}`)
    }
    function onWindowUp(e) {
      onWindowMove(e)   // final position from the release coords themselves
      window.removeEventListener('pointermove', onWindowMove)
      window.removeEventListener('pointerup', onWindowUp)
      window.removeEventListener('pointercancel', onWindowUp)
      splitInFlight = false
      hoverHint.set(null)
      postStateSnapshot()
    }
    window.addEventListener('pointermove', onWindowMove)
    window.addEventListener('pointerup', onWindowUp)
    window.addEventListener('pointercancel', onWindowUp)
  }

  function onCornerCommit(detail) {
    if (!detail || detail.kind !== 'collapse') return
    const neighborId = findNeighborPane($layout, paneId, detail.dir, detail.side)
    if (neighborId) collapsePane(neighborId)
    collapsePreview.set(null)
    postStateSnapshot()
  }

  function clampRatio(r) { return Math.max(0.1, Math.min(0.9, r)) }

  // ── cross-pane tab drag ───────────────────────────────────────────────────────
  import { moveTab, splitWithTab } from '../stores/layout.js'

  function onTabDragStart(tabId, label) {
    tabDrag.set({ tabId, fromPaneId: paneId, label })
  }
  function onTabDragEnd() {
    tabDrag.set(null)
    edgeZone = null
  }

  function onPaneDragOver(e) {
    const isTabDrag  = $tabDrag  && $tabDrag.fromPaneId  !== paneId
    const isItemDrag = $itemDrag && $itemDrag.fromPaneId !== paneId
    if (!isTabDrag && !isItemDrag) return
    e.preventDefault()
    if (isItemDrag) itemDragHover = true
    if (!paneEl || !isTabDrag) return
    const rect = paneEl.getBoundingClientRect()
    const x = (e.clientX - rect.left) / rect.width
    const y = (e.clientY - rect.top)  / rect.height
    const edge = 0.2
    if      (x < edge)       edgeZone = 'left'
    else if (x > 1 - edge)   edgeZone = 'right'
    else if (y < edge)       edgeZone = 'top'
    else if (y > 1 - edge)   edgeZone = 'bottom'
    else                     edgeZone = 'center'
  }

  function onPaneDragLeave(e) {
    if (!paneEl?.contains(e.relatedTarget)) { edgeZone = null; itemDragHover = false }
  }

  function onPaneDrop(e) {
    e.preventDefault()
    if ($tabDrag && $tabDrag.fromPaneId !== paneId && edgeZone) {
      const { tabId, fromPaneId } = $tabDrag
      if (edgeZone === 'center') {
        moveTab(fromPaneId, tabId, paneId)
      } else {
        const dir  = (edgeZone === 'left' || edgeZone === 'right') ? 'h' : 'v'
        const side = (edgeZone === 'left' || edgeZone === 'top')   ? 'before' : 'after'
        splitWithTab(paneId, dir, side, fromPaneId, tabId)
      }
      tabDrag.set(null)
      edgeZone = null
      postStateSnapshot()
      return
    }
    if ($itemDrag && $itemDrag.fromPaneId !== paneId && activeTabId) {
      const { type, id, ids, fromPaneId: fp, fromTabId: ft } = $itemDrag
      moveCrossPaneItem(fp, ft, ids ?? [id], type, paneId, activeTabId)
      itemDrag.set(null)
      itemDragHover = false
      postStateSnapshot()
      return
    }
    edgeZone = null
    itemDragHover = false
  }

  $: canDragTabs   = true
  $: isDraggingTab = $tabDrag !== null
  $: isDropTarget  = isDraggingTab && $tabDrag?.fromPaneId !== paneId
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div
  class="pane"
  data-pane-id={paneId}
  bind:this={paneEl}
  style="border: 1px solid {edgeTint}"
  class:drop-target={isDropTarget || itemDragHover}
  on:dragover={onPaneDragOver}
  on:dragleave={onPaneDragLeave}
  on:drop={onPaneDrop}
  on:contextmenu={onPaneContextMenu}
>
  {#if $mode === 'edit'}
    <CornerHandle corner="tl" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
    <CornerHandle corner="tr" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
    <CornerHandle corner="bl" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
    <CornerHandle corner="br" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
  {/if}

  <header>
    <TabBar
      {tabs} {activeTabId} mode={$mode}
      isDragging={activeDrag !== null}
      dropTabId={dropTarget?.type === 'tab' ? dropTarget.id : null}
      {canDragTabs}
      on:select={e        => { setActive(e.detail); clearSelection() }}
      on:rename={e        => renameTab(e.detail.id, e.detail.label)}
      on:setColor={e      => setTabColor(e.detail.id, e.detail.color)}
      on:remove={e        => removeTab(e.detail)}
      on:add={addTab}
      on:tabDragOver={e   => setDropTarget('tab', e.detail)}
      on:tabDragLeave={e  => clearDropTarget('tab', e.detail)}
      on:tabDrop={e       => executeDrop('tab', e.detail)}
      on:tabDragStart={e  => onTabDragStart(e.detail.tabId, e.detail.label)}
      on:tabDragEnd={onTabDragEnd}
      on:capture={() => postToCs({ type: 'capture', tabId: activeTabId })}
    />

    {#if $mode === 'edit' && selectedIds.size > 0}
      <ActionBar
        count={selectedIds.size} {tabs} activeTabId={activeTabId}
        on:moveTo={e         => moveSelectedTo(e.detail)}
        on:groupSelected={groupSelected}
        on:clearSelection={clearSelection}
      />
    {/if}
  </header>

  <section class="content"
    style="--name-col-w: {nameColWidth}px"
    on:dragenter|preventDefault={e => activeDrag && (e.dataTransfer.dropEffect = 'move')}
    on:dragover|preventDefault={e  => activeDrag && (e.dataTransfer.dropEffect = 'move')}
    on:mouseenter={() => $mode === 'edit' && hoverHint.set('Right-click: split or close this pane')}
    on:mouseleave={() => hoverHint.set(null)}
  >
    {#if !activeTab || (activeTab.sliders.length === 0 && activeTab.groups.length === 0)}
      <div class="empty">
        {#if $mode === 'edit'}
          Select sliders in Grasshopper, then click <strong>Capture</strong>.
        {:else}
          No sliders in this tab.
        {/if}
      </div>
    {:else}
      {#each activeTab.sliders as slider, i (slider.id)}
        <div class="row-slot" animate:flip={{ duration: slider.id === resizingSliderId ? 0 : 150, easing: cubicOut }}>
          <svelte:component
            this={ROW_COMPONENTS[slider.type] ?? SliderRow}
            {slider} mode={$mode}
            selected={selectedIds.has(slider.id)}
            isFirst={i === 0}
            isLast={i === activeTab.sliders.length - 1}
            on:change={e      => onSliderChange(slider.id, e.detail, slider.type, slider.multiSelect)}
            on:commit={e      => onSliderCommit(slider.id, e.detail, slider.type)}
            on:resize={e       => onPanelResize(slider.id, e.detail, false)}
            on:resizeCommit={e => onPanelResize(slider.id, e.detail, true)}
            on:resizeStart={e  => resizingSliderId = e.detail}
            on:resizeEnd={()   => resizingSliderId = null}
            on:select={e      => onSliderSelect(slider.id, e.detail)}
            on:remove={() => removeSlider(activeTab.id, slider.id)}
            on:dragStart={() => startDrag('slider', slider.id, activeTab.id, null)}
            on:rowDragOver={e => setDropTarget('slider-row', slider.id, e.detail, null)}
            on:rowDragLeave={() => clearDropTarget('slider-row', slider.id)}
            on:rowDrop={e     => executeDrop('slider-row', slider.id, e.detail)}
            on:dragEnd={endDrag}
          />
        </div>
      {/each}

      {#each activeTab.groups as group (group.id)}
        <GroupSection
          {group} mode={$mode} {selectedIds} {dropTarget} {resizingSliderId} {activeDrag}
          dropHighlight={dropTarget?.type === 'group-header' && dropTarget.id === group.id && activeDrag?.type === 'slider'}
          dropBefore={dropTarget?.type === 'group-header' && dropTarget.id === group.id && activeDrag?.type === 'group'}
          on:toggle={e              => toggleGroup(e.detail)}
          on:rename={e              => renameGroup(e.detail.id, e.detail.label)}
          on:remove={e              => removeGroup(e.detail)}
          on:sliderChange={e        => onSliderChange(e.detail.id, e.detail.value, e.detail.type, e.detail.multiSelect)}
          on:sliderCommit={e        => onSliderCommit(e.detail.id, e.detail.value, e.detail.type)}
          on:sliderResize={e        => onPanelResize(e.detail.id, e.detail.height, false)}
          on:sliderResizeCommit={e  => onPanelResize(e.detail.id, e.detail.height, true)}
          on:sliderResizeStart={e   => resizingSliderId = e.detail}
          on:sliderResizeEnd={()    => resizingSliderId = null}
          on:sliderSelect={e        => onSliderSelect(e.detail.id, e.detail.multi)}
          on:sliderRemove={e        => removeSlider(activeTab.id, e.detail.sliderId)}
          on:headerDragStart={() => startDrag('group', group.id, activeTab.id, null)}
          on:headerDragOver={() => setDropTarget('group-header', group.id)}
          on:headerDragLeave={() => clearDropTarget('group-header', group.id)}
          on:headerDrop={() => executeDrop('group-header', group.id)}
          on:groupDragEnd={endDrag}
          on:sliderDragStart={e      => startDrag('slider', e.detail.sliderId, activeTab.id, e.detail.groupId)}
          on:sliderDragEnd={endDrag}
          on:sliderRowDragOver={e    => setDropTarget('slider-row', e.detail.sliderId, e.detail.pos, e.detail.groupId)}
          on:sliderRowDragLeave={e   => clearDropTarget('slider-row', e.detail.sliderId)}
          on:sliderRowDrop={e        => executeDrop('slider-row', e.detail.sliderId, e.detail.pos)}
          on:capture={e              => postToCs({ type: 'capture', tabId: activeTabId, groupId: e.detail.groupId })}
        />
      {/each}
    {/if}
  </section>

  <!-- collapse intent overlay (shown on the pane that WOULD be collapsed) -->
  {#if $collapsePreview === paneId}
    <div class="intent-overlay" style="border: 2px solid {dangerTint}; background: {dangerFill}"></div>
  {/if}

  <!-- cross-pane item drag overlay (only when hovering) -->
  {#if itemDragHover}
    <div class="drop-overlay zone-center"></div>
  {/if}

  <!-- cross-pane tab drop overlay -->
  {#if isDropTarget}
    <div class="drop-overlay"
      class:zone-left={edgeZone === 'left'}
      class:zone-right={edgeZone === 'right'}
      class:zone-top={edgeZone === 'top'}
      class:zone-bottom={edgeZone === 'bottom'}
      class:zone-center={edgeZone === 'center'}
    >
      {#if edgeZone && edgeZone !== 'center'}
        <div class="split-preview split-{edgeZone}"></div>
      {/if}
    </div>
  {/if}
</div>

{#if contextMenu}
  <ContextMenu x={contextMenu.x} y={contextMenu.y} items={contextMenu.items} on:close={() => contextMenu = null} />
{/if}

<style>
  .pane {
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
    position: relative;
    overflow: hidden;
    border-radius: 4px;
    background: var(--bg);
    /* border colour set inline from edgeTint (JS/$theme-driven, not a CSS
       var — see edgeTint's comment above) */
  }

  header {
    flex-shrink: 0;
    background: var(--bg);
  }

  .content {
    flex: 1;
    overflow-y: auto;
    padding: 6px 0;
  }
  .row-slot { display: block; }
  .content::-webkit-scrollbar       { width: 4px; }
  .content::-webkit-scrollbar-track { background: transparent; }
  .content::-webkit-scrollbar-thumb { background: var(--grid); border-radius: 2px; }

  .empty {
    padding: 36px 20px;
    color: rgba(var(--text-rgb), 0.3);
    font-size: 12px;
    line-height: 1.7;
  }
  .empty strong { color: rgba(var(--text-rgb), 0.45); font-weight: 500; }

  /* corner collapse intent overlay — border/background colour come from
     dangerTint/dangerFill (JS, theme-driven) via inline style, not CSS vars,
     see edgeTint's note */
  .intent-overlay {
    position: absolute;
    inset: 0;
    pointer-events: none;
    z-index: 15;
  }

  /* cross-pane drop overlay */
  .drop-overlay {
    position: absolute;
    inset: 0;
    pointer-events: none;
    z-index: 15;
    border: 2px solid transparent;
    transition: border-color 0.1s;
  }
  .drop-target .drop-overlay  { border-color: rgba(var(--accent-rgb), 0.27); }
  .zone-center .drop-overlay,
  .drop-overlay.zone-center   { border-color: var(--accent); background: rgba(var(--accent-rgb), 0.07); }

  .split-preview {
    position: absolute;
    background: rgba(var(--accent-rgb), 0.2);
    border: 2px solid var(--accent);
    pointer-events: none;
  }
  .split-preview.split-left   { inset: 0 50% 0 0; }
  .split-preview.split-right  { inset: 0 0 0 50%; }
  .split-preview.split-top    { inset: 0 0 50% 0; }
  .split-preview.split-bottom { inset: 50% 0 0 0; }
</style>
