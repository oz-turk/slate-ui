<script>
  import { onMount } from 'svelte'
  import TabBar       from './lib/TabBar.svelte'
  import SliderRow    from './lib/SliderRow.svelte'
  import GroupSection from './lib/GroupSection.svelte'
  import EditToolbar  from './lib/EditToolbar.svelte'
  import ActionBar    from './lib/ActionBar.svelte'

  // ── state ──────────────────────────────────────────────────────────────────
  let mode   = 'preview'
  let pinned = true

  let tabs        = [{ id: 'main', label: 'Main', sliders: [], groups: [] }]
  let activeTabId = 'main'
  let selectedIds = new Set()

  $: activeTab     = tabs.find(t => t.id === activeTabId) ?? tabs[0]
  $: selectedCount = selectedIds.size

  // ── drag state ─────────────────────────────────────────────────────────────
  let activeDrag  = null  // { type:'slider'|'group', id, fromTabId, fromGroupId }
  let dropTarget  = null  // { type:'slider-row'|'group-header'|'tab', id, pos? }

  function startDrag(type, id, fromTabId, fromGroupId = null) {
    activeDrag = { type, id, fromTabId, fromGroupId }
  }

  function endDrag() {
    activeDrag = null
    dropTarget = null
  }

  function setDropTarget(type, id, pos = null) {
    if (!activeDrag) return
    dropTarget = { type, id, pos }
  }

  function clearDropTarget(type, id) {
    if (dropTarget?.type === type && dropTarget.id === id) dropTarget = null
  }

  function executeDrop(type, id, pos) {
    if (!activeDrag) return
    const { type: dragType, id: dragId, fromTabId, fromGroupId } = activeDrag

    if (type === 'tab') {
      if (fromTabId === id) { endDrag(); return }
      if (dragType === 'slider') moveSliderToTab(dragId, fromTabId, fromGroupId, id)
      else                       moveGroupToTab(dragId, fromTabId, id)
    } else if (type === 'slider-row') {
      if (dragType === 'slider') reorderSlider(dragId, fromTabId, fromGroupId, id, pos)
    } else if (type === 'group-header') {
      if (dragType === 'slider') moveSliderToGroup(dragId, fromTabId, fromGroupId, id)
      else if (dragType === 'group') reorderGroup(dragId, fromTabId, id)
    }

    endDrag()
    postStateSnapshot()
  }

  // ── drag operations ────────────────────────────────────────────────────────
  function extractSlider(sliderId, fromTabId, fromGroupId) {
    let extracted = null
    tabs = tabs.map(t => {
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
    })
    return extracted
  }

  function moveSliderToTab(sliderId, fromTabId, fromGroupId, toTabId) {
    const slider = extractSlider(sliderId, fromTabId, fromGroupId)
    if (!slider) return
    tabs = tabs.map(t => t.id === toTabId ? { ...t, sliders: [...t.sliders, slider] } : t)
  }

  function moveGroupToTab(groupId, fromTabId, toTabId) {
    let group = null
    tabs = tabs.map(t => {
      if (t.id !== fromTabId) return t
      group = t.groups.find(g => g.id === groupId)
      return { ...t, groups: t.groups.filter(g => g.id !== groupId) }
    })
    if (!group) return
    tabs = tabs.map(t => t.id === toTabId ? { ...t, groups: [...t.groups, group] } : t)
  }

  function reorderSlider(dragId, fromTabId, fromGroupId, targetId, pos) {
    if (fromGroupId || dragId === targetId) return
    tabs = tabs.map(t => {
      if (t.id !== fromTabId) return t
      const sliders = [...t.sliders]
      const fromIdx = sliders.findIndex(s => s.id === dragId)
      if (fromIdx < 0 || !sliders.find(s => s.id === targetId)) return t
      const [moved] = sliders.splice(fromIdx, 1)
      const toIdx   = sliders.findIndex(s => s.id === targetId)
      sliders.splice(pos === 'after' ? toIdx + 1 : toIdx, 0, moved)
      return { ...t, sliders }
    })
  }

  function reorderGroup(dragId, fromTabId, targetId) {
    if (dragId === targetId) return
    tabs = tabs.map(t => {
      if (t.id !== fromTabId) return t
      const groups  = [...t.groups]
      const fromIdx = groups.findIndex(g => g.id === dragId)
      if (fromIdx < 0 || !groups.find(g => g.id === targetId)) return t
      const [moved] = groups.splice(fromIdx, 1)
      const toIdx   = groups.findIndex(g => g.id === targetId)
      groups.splice(toIdx, 0, moved)
      return { ...t, groups }
    })
  }

  function moveSliderToGroup(sliderId, fromTabId, fromGroupId, targetGroupId) {
    if (fromGroupId === targetGroupId) return
    const slider = extractSlider(sliderId, fromTabId, fromGroupId)
    if (!slider) return
    tabs = tabs.map(t => {
      if (t.id !== fromTabId) return t
      return { ...t, groups: t.groups.map(g =>
        g.id === targetGroupId ? { ...g, sliders: [...g.sliders, slider] } : g
      )}
    })
  }

  // ── C# → JS messages ───────────────────────────────────────────────────────
  onMount(() => {
    window.chrome?.webview?.addEventListener('message', e => {
      try { handleMessage(JSON.parse(e.data)) } catch {}
    })
    // Signal to C# that the page is ready to receive messages
    window.chrome?.webview?.postMessage({ type: 'ui_ready' })
  })

  function allSliderIds() {
    const ids = new Set()
    for (const t of tabs) {
      t.sliders.forEach(s => ids.add(s.id))
      t.groups.forEach(g => g.sliders.forEach(s => ids.add(s.id)))
    }
    return ids
  }

  function handleMessage(msg) {
    if (msg.type === 'slider_added') {
      if (allSliderIds().has(msg.id)) return  // dedup
      const slider = { id: msg.id, name: msg.name, min: msg.min, max: msg.max, value: msg.value }
      const target = tabs.find(t => t.id === msg.tabId)
                  ?? tabs.find(t => t.label.toLowerCase() === (msg.tabId ?? '').toLowerCase())
                  ?? tabs.find(t => t.id === activeTabId)
      const targetId = target?.id ?? activeTabId
      tabs = tabs.map(t => t.id === targetId ? { ...t, sliders: [...t.sliders, slider] } : t)
      postStateSnapshot()
    }
    if (msg.type === 'cleared') {
      tabs = tabs.map(t => ({ ...t, sliders: [], groups: [] }))
      postStateSnapshot()
    }
    if (msg.type === 'restore_state') {
      tabs = (msg.tabs ?? []).map(t => ({
        ...t,
        sliders: t.sliders ?? [],
        groups:  (t.groups ?? []).map(g => ({ ...g, sliders: g.sliders ?? [] }))
      }))
      if (msg.activeTabId) activeTabId = msg.activeTabId
      postStateSnapshot()  // confirm restored state back to C#
    }
  }

  // ── JS → C# ────────────────────────────────────────────────────────────────
  function postToCs(obj) {
    window.chrome?.webview?.postMessage(obj)
  }

  function postStateSnapshot() {
    postToCs({
      type: 'state_snapshot',
      activeTabId,
      tabs: tabs.map(t => ({
        id: t.id, label: t.label,
        sliders: t.sliders.map(s => ({ id: s.id, name: s.name, min: s.min, max: s.max })),
        groups:  t.groups.map(g => ({
          id: g.id, label: g.label, collapsed: g.collapsed,
          sliders: g.sliders.map(s => ({ id: s.id, name: s.name, min: s.min, max: s.max }))
        }))
      }))
    })
  }

  function _updateLocalValue(sliderId, value) {
    tabs = tabs.map(t => ({
      ...t,
      sliders: t.sliders.map(s => s.id === sliderId ? { ...s, value } : s),
      groups:  t.groups.map(g => ({ ...g, sliders: g.sliders.map(s => s.id === sliderId ? { ...s, value } : s) }))
    }))
  }

  function onSliderChange(sliderId, value) {
    _updateLocalValue(sliderId, value)
    postToCs({ type: 'slider_change', id: sliderId, value })
  }

  function onSliderCommit(sliderId, value) {
    _updateLocalValue(sliderId, value)
    postToCs({ type: 'slider_change', id: sliderId, value })
  }

  // ── selection ───────────────────────────────────────────────────────────────
  function onSliderSelect(sliderId, multi) {
    if (mode !== 'edit') return
    if (multi) {
      const next = new Set(selectedIds)
      if (next.has(sliderId)) next.delete(sliderId)
      else next.add(sliderId)
      selectedIds = next
    } else {
      selectedIds = selectedIds.has(sliderId) && selectedIds.size === 1
        ? new Set()
        : new Set([sliderId])
    }
  }

  function clearSelection() { selectedIds = new Set() }

  // ── ActionBar operations ────────────────────────────────────────────────────
  function moveSelectedTo(targetTabId) {
    if (!selectedIds.size) return
    const toMove = []
    tabs = tabs.map(t => {
      if (t.id !== activeTab.id) return t
      const kept   = t.sliders.filter(s => { if (selectedIds.has(s.id)) { toMove.push(s); return false } return true })
      const groups = t.groups.map(g => {
        const gKept = g.sliders.filter(s => { if (selectedIds.has(s.id)) { toMove.push(s); return false } return true })
        return { ...g, sliders: gKept }
      })
      return { ...t, sliders: kept, groups }
    })
    tabs = tabs.map(t => t.id === targetTabId ? { ...t, sliders: [...t.sliders, ...toMove] } : t)
    selectedIds = new Set()
    postStateSnapshot()
  }

  function groupSelected() {
    if (!selectedIds.size) return
    const groupSliders = []
    tabs = tabs.map(t => {
      if (t.id !== activeTab.id) return t
      const remaining = t.sliders.filter(s => { if (selectedIds.has(s.id)) { groupSliders.push(s); return false } return true })
      const newGroup  = { id: 'g_' + Date.now(), label: 'Group', collapsed: false, sliders: groupSliders }
      return { ...t, sliders: remaining, groups: [...t.groups, newGroup] }
    })
    selectedIds = new Set()
    postStateSnapshot()
  }

  // ── tab actions ─────────────────────────────────────────────────────────────
  function addTab() {
    const id = 'tab_' + Date.now()
    tabs = [...tabs, { id, label: 'Tab ' + (tabs.length + 1), sliders: [], groups: [] }]
    activeTabId = id
    postStateSnapshot()
  }

  function renameTab(id, label) {
    tabs = tabs.map(t => t.id === id ? { ...t, label } : t)
    postStateSnapshot()
  }

  function removeTab(id) {
    if (tabs.length === 1) return
    const next = tabs.find(t => t.id !== id)
    tabs = tabs.filter(t => t.id !== id)
    if (activeTabId === id) activeTabId = next.id
    postStateSnapshot()
  }

  // ── slider/group actions ────────────────────────────────────────────────────
  function removeSlider(tabId, sliderId) {
    tabs = tabs.map(t => {
      if (t.id !== tabId) return t
      return {
        ...t,
        sliders: t.sliders.filter(s => s.id !== sliderId),
        groups:  t.groups.map(g => ({ ...g, sliders: g.sliders.filter(s => s.id !== sliderId) }))
      }
    })
    const next = new Set(selectedIds); next.delete(sliderId); selectedIds = next
    postStateSnapshot()
  }

  function toggleGroup(groupId) {
    tabs = tabs.map(t => ({ ...t, groups: t.groups.map(g => g.id === groupId ? { ...g, collapsed: !g.collapsed } : g) }))
    postStateSnapshot()
  }

  function renameGroup(groupId, label) {
    tabs = tabs.map(t => ({ ...t, groups: t.groups.map(g => g.id === groupId ? { ...g, label } : g) }))
    postStateSnapshot()
  }

  function removeGroup(groupId) {
    tabs = tabs.map(t => {
      if (t.id !== activeTab.id) return t
      const group    = t.groups.find(g => g.id === groupId)
      const promoted = group?.sliders ?? []
      return { ...t, sliders: [...t.sliders, ...promoted], groups: t.groups.filter(g => g.id !== groupId) }
    })
    postStateSnapshot()
  }
</script>

<main class:edit={mode === 'edit'}>
  <header>
    <EditToolbar
      bind:mode bind:pinned
      on:addTab={addTab}
      on:pin={e  => postToCs({ type: 'pin', value: e.detail })}
      on:capture={() => postToCs({ type: 'capture', tabId: activeTabId })}
    />
    <TabBar
      {tabs} {activeTabId} {mode}
      isDragging={activeDrag !== null}
      dropTabId={dropTarget?.type === 'tab' ? dropTarget.id : null}
      on:select={e        => { activeTabId = e.detail; clearSelection() }}
      on:rename={e        => renameTab(e.detail.id, e.detail.label)}
      on:remove={e        => removeTab(e.detail)}
      on:add={addTab}
      on:tabDragOver={e   => setDropTarget('tab', e.detail)}
      on:tabDragLeave={e  => clearDropTarget('tab', e.detail)}
      on:tabDrop={e       => executeDrop('tab', e.detail)}
    />
    {#if mode === 'edit'}
      <ActionBar
        count={selectedCount} {tabs} {activeTabId}
        on:moveTo={e         => moveSelectedTo(e.detail)}
        on:groupSelected={groupSelected}
        on:clearSelection={clearSelection}
      />
    {/if}
  </header>

  <section class="content">
    {#if activeTab?.sliders.length === 0 && activeTab?.groups.length === 0}
      <div class="empty">
        {#if mode === 'edit'}
          Select sliders in Grasshopper, then click <strong>Capture</strong>.
        {:else}
          No sliders in this tab.
        {/if}
      </div>
    {:else}
      {#each activeTab.sliders as slider (slider.id)}
        <SliderRow
          {slider} {mode}
          selected={selectedIds.has(slider.id)}
          dropAbove={dropTarget?.type === 'slider-row' && dropTarget.id === slider.id && dropTarget.pos === 'before'}
          dropBelow={dropTarget?.type === 'slider-row' && dropTarget.id === slider.id && dropTarget.pos === 'after'}
          on:change={e      => onSliderChange(slider.id, e.detail)}
          on:commit={e      => onSliderCommit(slider.id, e.detail)}
          on:select={e      => onSliderSelect(slider.id, e.detail)}
          on:remove={() => removeSlider(activeTab.id, slider.id)}
          on:dragStart={() => startDrag('slider', slider.id, activeTab.id, null)}
          on:rowDragOver={e => setDropTarget('slider-row', slider.id, e.detail)}
          on:rowDragLeave={() => clearDropTarget('slider-row', slider.id)}
          on:rowDrop={e     => executeDrop('slider-row', slider.id, e.detail)}
          on:dragEnd={endDrag}
        />
      {/each}

      {#each activeTab.groups as group (group.id)}
        <GroupSection
          {group} {mode} {selectedIds}
          dropHighlight={dropTarget?.type === 'group-header' && dropTarget.id === group.id && activeDrag?.type === 'slider'}
          dropBefore={dropTarget?.type === 'group-header' && dropTarget.id === group.id && activeDrag?.type === 'group'}
          on:toggle={e          => toggleGroup(e.detail)}
          on:rename={e          => renameGroup(e.detail.id, e.detail.label)}
          on:remove={e          => removeGroup(e.detail)}
          on:sliderChange={e    => onSliderChange(e.detail.id, e.detail.value)}
          on:sliderCommit={e    => onSliderCommit(e.detail.id, e.detail.value)}
          on:sliderSelect={e    => onSliderSelect(e.detail.id, e.detail.multi)}
          on:sliderRemove={e    => removeSlider(activeTab.id, e.detail.sliderId)}
          on:headerDragStart={() => startDrag('group', group.id, activeTab.id, null)}
          on:headerDragOver={() => setDropTarget('group-header', group.id)}
          on:headerDragLeave={() => clearDropTarget('group-header', group.id)}
          on:headerDrop={() => executeDrop('group-header', group.id)}
          on:groupDragEnd={endDrag}
        />
      {/each}
    {/if}
  </section>
</main>

<style>
  :global(*, *::before, *::after) { box-sizing: border-box; margin: 0; padding: 0; }

  :global(body) {
    background: #111;
    color: #e2e2e2;
    font-family: 'Segoe UI', system-ui, sans-serif;
    font-size: 13px;
    overflow: hidden;
    user-select: none;
    -webkit-font-smoothing: antialiased;
  }

  main {
    display: flex;
    flex-direction: column;
    height: 100vh;
  }
  main.edit { outline: 1px solid #3b7fff33; }

  header {
    flex-shrink: 0;
    background: #161616;
    border-bottom: 1px solid #252525;
  }

  .content {
    flex: 1;
    overflow-y: auto;
    padding: 6px 0;
  }
  .content::-webkit-scrollbar       { width: 4px; }
  .content::-webkit-scrollbar-track { background: transparent; }
  .content::-webkit-scrollbar-thumb { background: #2e2e2e; border-radius: 2px; }

  .empty {
    padding: 36px 20px;
    color: #444;
    font-size: 12px;
    line-height: 1.7;
  }
  .empty strong { color: #666; font-weight: 500; }
</style>
