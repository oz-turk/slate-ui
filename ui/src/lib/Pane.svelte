<script>
  import TabBar       from './TabBar.svelte'
  import SliderRow    from './SliderRow.svelte'
  import GroupSection from './GroupSection.svelte'
  import ActionBar    from './ActionBar.svelte'
  import CornerHandle from './CornerHandle.svelte'
  import { layout, updatePane, findLeaf, newTabId, splitPane, collapsePane, findNeighborPane, moveCrossPaneItem } from '../stores/layout.js'
  import { tabDrag, itemDrag, collapsePreview } from '../stores/dragState.js'
  import { mode } from '../stores/uiState.js'
  import { postToCs, postStateSnapshot } from './ipc.js'
  import { flip } from 'svelte/animate'
  import { cubicOut } from 'svelte/easing'

  export let paneId

  // ── derive pane state reactively (not derived() — paneId must stay live) ─────
  $: pane        = findLeaf($layout, paneId)
  $: tabs        = pane?.tabs        ?? []
  $: activeTabId = pane?.activeTabId ?? null
  $: activeTab   = tabs.find(t => t.id === activeTabId) ?? tabs[0]

  // ── local UI state ────────────────────────────────────────────────────────────
  let selectedIds   = new Set()
  let activeDrag    = null
  let dropTarget    = null
  let lastReorderKey = null  // dedupe key so hovering the same slider-row spot doesn't re-splice every event
  let reorderRaf      = null // pending rAF for the deferred live reorder (see setDropTarget)
  let splitPreview = null   // { dir, side, ratio } | null — live preview while dragging

  // ── tab drag (cross-pane) drop state ─────────────────────────────────────────
  let edgeZone     = null   // 'left' | 'right' | 'top' | 'bottom' | 'center' | null
  let itemDragHover = false  // true only while a cross-pane item drag is hovering this pane
  let paneEl

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
  function updateSliderInGroups(groups, sliderId, value) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.map(s => s.id === sliderId ? { ...s, value } : s),
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
        sliders: t.sliders.map(s => s.id === sliderId ? { ...s, value } : s),
        groups:  updateSliderInGroups(t.groups, sliderId, value)
      }))
    }))
  }

  function onSliderChange(sliderId, value) {
    updateSliderValue(sliderId, value)
    postToCs({ type: 'slider_change', id: sliderId, value })
  }
  function onSliderCommit(sliderId, value) {
    updateSliderValue(sliderId, value)
    postToCs({ type: 'slider_change', id: sliderId, value })
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
    activeDrag = { type, id, fromTabId, fromGroupId }
    itemDrag.set({ type, id, fromPaneId: paneId, fromTabId, fromGroupId })
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
    const { type: dt, id: di, fromTabId, fromGroupId } = activeDrag
    if (type === 'tab') {
      if (fromTabId === id) { endDrag(); return }
      if (dt === 'slider') moveSliderToTab(di, fromTabId, fromGroupId, id)
      else                 moveGroupToTab(di, fromTabId, id)
    } else if (type === 'group-header') {
      if (dt === 'slider') moveSliderToGroup(di, fromTabId, fromGroupId, id)
      else if (dt === 'group') nestGroupInGroup(di, fromTabId, id)
    }
    // slider-row reordering already happened live during dragover — drop just finalizes
    endDrag()
    postStateSnapshot()
  }

  function withTab(tabId, fn) {
    mutateTabs(tabs => tabs.map(t => t.id === tabId ? fn(t) : t))
  }

  function extractSlider(sliderId, fromTabId, fromGroupId) {
    let extracted = null
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== fromTabId) return t
      if (fromGroupId) {
        return { ...t, groups: t.groups.map(g => {
          if (g.id !== fromGroupId) return g
          extracted = g.sliders.find(s => s.id === sliderId)
          return { ...g, sliders: g.sliders.filter(s => s.id !== sliderId) }
        })}
      } else {
        extracted = t.sliders.find(s => s.id === sliderId)
        return { ...t, sliders: t.sliders.filter(s => s.id !== sliderId) }
      }
    }))
    return extracted
  }

  function moveSliderToTab(sliderId, fromTabId, fromGroupId, toTabId) {
    let slider = null
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        if (fromGroupId) {
          return { ...t, groups: t.groups.map(g => {
            if (g.id !== fromGroupId) return g
            slider = g.sliders.find(s => s.id === sliderId)
            return { ...g, sliders: g.sliders.filter(s => s.id !== sliderId) }
          })}
        }
        slider = t.sliders.find(s => s.id === sliderId)
        return { ...t, sliders: t.sliders.filter(s => s.id !== sliderId) }
      })
      if (!slider) return tabs
      return t1.map(t => t.id === toTabId ? { ...t, sliders: [...t.sliders, slider] } : t)
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
  // visibly shift out of the way as the drag progresses. Handles moving across
  // group boundaries (or in/out of a group) the same way the old drop-time
  // reorder did — insert next to targetId, in whichever list (top-level or
  // targetGroupId's) it lives in.
  function liveReorderSlider(targetId, targetGroupId, pos) {
    if (!activeDrag || activeDrag.id === targetId) return
    const dragId      = activeDrag.id
    const fromGroupId = activeDrag.fromGroupId

    mutateTabsQuiet(tabs => tabs.map(t => {
      if (t.id !== activeDrag.fromTabId) return t

      let slider     = null
      let groups     = t.groups
      let topSliders = t.sliders

      if (fromGroupId) {
        groups = mapGroupTree(groups, fromGroupId, g => {
          slider = g.sliders.find(s => s.id === dragId)
          return { sliders: g.sliders.filter(s => s.id !== dragId) }
        })
      } else {
        slider     = topSliders.find(s => s.id === dragId)
        topSliders = topSliders.filter(s => s.id !== dragId)
      }
      if (!slider) return t

      if (targetGroupId) {
        groups = mapGroupTree(groups, targetGroupId, g => {
          const sliders = [...g.sliders]
          const toIdx = sliders.findIndex(s => s.id === targetId)
          sliders.splice(toIdx < 0 ? sliders.length : pos === 'after' ? toIdx + 1 : toIdx, 0, slider)
          return { sliders }
        })
      } else {
        const sliders = [...topSliders]
        const toIdx = sliders.findIndex(s => s.id === targetId)
        sliders.splice(toIdx < 0 ? sliders.length : pos === 'after' ? toIdx + 1 : toIdx, 0, slider)
        topSliders = sliders
      }

      return { ...t, sliders: topSliders, groups }
    }))

    activeDrag = { ...activeDrag, fromGroupId: targetGroupId }
  }

  function moveSliderToGroup(sliderId, fromTabId, fromGroupId, targetGroupId) {
    if (fromGroupId === targetGroupId) return
    let slider = null
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        if (fromGroupId) {
          return { ...t, groups: mapGroupTree(t.groups, fromGroupId, g => {
            slider = g.sliders.find(s => s.id === sliderId)
            return { sliders: g.sliders.filter(s => s.id !== sliderId) }
          })}
        }
        slider = t.sliders.find(s => s.id === sliderId)
        return { ...t, sliders: t.sliders.filter(s => s.id !== sliderId) }
      })
      if (!slider) return tabs
      return t1.map(t => t.id !== fromTabId ? t : {
        ...t,
        groups: mapGroupTree(t.groups, targetGroupId, g => ({ sliders: [...g.sliders, slider] }))
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
    const toMove = []
    mutateTabs(tabs => {
      const base = tabs.map(t => {
        if (t.id !== activeTab.id) return t
        const kept   = t.sliders.filter(s => { if (selectedIds.has(s.id)) { toMove.push(s); return false } return true })
        const groups = t.groups.map(g => {
          const gKept = g.sliders.filter(s => { if (selectedIds.has(s.id)) { toMove.push(s); return false } return true })
          return { ...g, sliders: gKept }
        })
        return { ...t, sliders: kept, groups }
      })
      return base.map(t => t.id === targetTabId ? { ...t, sliders: [...t.sliders, ...toMove] } : t)
    })
    selectedIds = new Set()
  }

  function groupSelected() {
    if (!selectedIds.size) return
    const groupSliders = []
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== activeTab.id) return t
      const remaining = t.sliders.filter(s => { if (selectedIds.has(s.id)) { groupSliders.push(s); return false } return true })
      const newGroup  = { id: 'g_' + Date.now(), label: 'Group', collapsed: false, sliders: groupSliders, groups: [] }
      return { ...t, sliders: remaining, groups: [...t.groups, newGroup] }
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
  function onCornerPreview(detail) {
    if (!detail) {
      collapsePreview.set(null)
      splitPreview = null
      return
    }
    if (detail.kind === 'collapse') {
      const neighborId = findNeighborPane($layout, paneId, detail.dir, detail.side)
      collapsePreview.set(neighborId ?? null)
      splitPreview = null
      return
    }
    // kind === 'split': update preview overlay only, DOM stays intact until commit
    const rect = paneEl.getBoundingClientRect()
    splitPreview = { dir: detail.dir, side: detail.side, ratio: clampRatio(ratioFromRect(detail, rect)) }
  }

  function onCornerCommit(detail) {
    if (!detail) return
    if (detail.kind === 'collapse') {
      const neighborId = findNeighborPane($layout, paneId, detail.dir, detail.side)
      if (neighborId) collapsePane(neighborId)
      collapsePreview.set(null)
      postStateSnapshot()
    } else if (detail.kind === 'split' && splitPreview) {
      splitPane(paneId, splitPreview.dir, splitPreview.side, splitPreview.ratio)
      splitPreview = null
      postStateSnapshot()
    }
  }

  function ratioFromRect({ dir, clientX, clientY }, rect) {
    return dir === 'h'
      ? (clientX - rect.left) / rect.width
      : (clientY - rect.top)  / rect.height
  }

  function clampRatio(r) { return Math.max(0.1, Math.min(0.9, r)) }

  function splitPreviewInset({ dir, side, ratio }) {
    if (dir === 'h') return side === 'before'
      ? `0 ${(1-ratio)*100}% 0 0`
      : `0 0 0 ${ratio*100}%`
    return side === 'before'
      ? `0 0 ${(1-ratio)*100}% 0`
      : `${ratio*100}% 0 0 0`
  }

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
      const { type, id, fromPaneId: fp, fromTabId: ft, fromGroupId: fg } = $itemDrag
      moveCrossPaneItem(fp, ft, fg, id, type, paneId, activeTabId)
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
  $: isDraggingItem = $itemDrag !== null && $itemDrag.fromPaneId !== paneId
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div
  class="pane"
  bind:this={paneEl}
  class:drop-target={isDropTarget || itemDragHover}
  on:dragover={onPaneDragOver}
  on:dragleave={onPaneDragLeave}
  on:drop={onPaneDrop}
>
  <CornerHandle corner="tl" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
  <CornerHandle corner="tr" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
  <CornerHandle corner="bl" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
  <CornerHandle corner="br" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />

  <header>
    <TabBar
      {tabs} {activeTabId} mode={$mode}
      isDragging={activeDrag !== null}
      dropTabId={dropTarget?.type === 'tab' ? dropTarget.id : null}
      {canDragTabs}
      on:select={e        => { setActive(e.detail); clearSelection() }}
      on:rename={e        => renameTab(e.detail.id, e.detail.label)}
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
    on:dragenter|preventDefault={e => activeDrag && (e.dataTransfer.dropEffect = 'move')}
    on:dragover|preventDefault={e  => activeDrag && (e.dataTransfer.dropEffect = 'move')}
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
        <div class="row-slot" animate:flip={{ duration: 150, easing: cubicOut }}>
          <SliderRow
            {slider} mode={$mode}
            selected={selectedIds.has(slider.id)}
            isFirst={i === 0}
            isLast={i === activeTab.sliders.length - 1}
            on:change={e      => onSliderChange(slider.id, e.detail)}
            on:commit={e      => onSliderCommit(slider.id, e.detail)}
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
          {group} mode={$mode} {selectedIds} {dropTarget}
          dropHighlight={dropTarget?.type === 'group-header' && dropTarget.id === group.id && activeDrag?.type === 'slider'}
          dropBefore={dropTarget?.type === 'group-header' && dropTarget.id === group.id && activeDrag?.type === 'group'}
          on:toggle={e              => toggleGroup(e.detail)}
          on:rename={e              => renameGroup(e.detail.id, e.detail.label)}
          on:remove={e              => removeGroup(e.detail)}
          on:sliderChange={e        => onSliderChange(e.detail.id, e.detail.value)}
          on:sliderCommit={e        => onSliderCommit(e.detail.id, e.detail.value)}
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

  <!-- split preview overlay (shows where the new pane will appear) -->
  {#if splitPreview}
    <div class="split-preview-pane" style="inset: {splitPreviewInset(splitPreview)}"></div>
  {/if}

  <!-- collapse intent overlay (shown on the pane that WOULD be collapsed) -->
  {#if $collapsePreview === paneId}
    <div class="intent-overlay intent-collapse"></div>
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

<style>
  .pane {
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
    overflow: hidden;
    position: relative;
    background: var(--bg);
  }

  header {
    flex-shrink: 0;
    background: var(--panel-bg);
    border-bottom: 1px solid var(--grid);
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

  /* split preview — new pane area highlighted */
  .split-preview-pane {
    position: absolute;
    pointer-events: none;
    z-index: 15;
    background: rgba(var(--accent-rgb), 0.1);
    border: 2px solid rgba(var(--accent-rgb), 0.4);
  }

  /* corner collapse intent overlay */
  .intent-overlay {
    position: absolute;
    inset: 0;
    pointer-events: none;
    z-index: 15;
    border: 2px solid;
  }
  .intent-collapse { background: rgba(var(--danger-rgb), 0.1); border-color: rgba(var(--danger-rgb), 0.53); }

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
