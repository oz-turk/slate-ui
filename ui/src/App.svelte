<script>
  import { onMount }    from 'svelte'
  import { get }        from 'svelte/store'
  import PaneLayout     from './lib/PaneLayout.svelte'
  import EditToolbar    from './lib/EditToolbar.svelte'
  import { layout, allLeaves, restoreLayout, updatePane, makeLeaf } from './stores/layout.js'
  import { mode, pinned } from './stores/uiState.js'
  import { postToCs, postStateSnapshot } from './lib/ipc.js'

  // ── C# ↔ JS ───────────────────────────────────────────────────────────────────
  onMount(() => {
    window.chrome?.webview?.addEventListener('message', e => {
      try { handleMessage(JSON.parse(e.data)) } catch {}
    })
    postToCs({ type: 'ui_ready' })
  })

  function allSliderIds() {
    const ids = new Set()
    function collectGroups(groups) {
      for (const g of groups) {
        g.sliders.forEach(s => ids.add(s.id))
        collectGroups(g.groups ?? [])
      }
    }
    for (const leaf of allLeaves(get(layout)))
      for (const t of leaf.tabs) {
        t.sliders.forEach(s => ids.add(s.id))
        collectGroups(t.groups)
      }
    return ids
  }

  function addSliderToGroupInTree(groups, groupId, slider) {
    return groups.map(g =>
      g.id === groupId
        ? { ...g, sliders: [...g.sliders, slider] }
        : { ...g, groups: addSliderToGroupInTree(g.groups ?? [], groupId, slider) }
    )
  }

  function updateNameInGroupTree(groups, id, name) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.map(s => s.id === id ? { ...s, name } : s),
      groups: updateNameInGroupTree(g.groups ?? [], id, name)
    }))
  }

  function handleMessage(msg) {
    if (msg.type === 'slider_added') {
      if (allSliderIds().has(msg.id)) return
      const slider = { id: msg.id, name: msg.name, min: msg.min, max: msg.max, value: msg.value }
      const $l  = get(layout)
      const leaves = allLeaves($l)
      let targetPaneId = null, targetTabId = null
      for (const leaf of leaves) {
        const t = leaf.tabs.find(t => t.id === msg.tabId)
               ?? leaf.tabs.find(t => t.label.toLowerCase() === (msg.tabId ?? '').toLowerCase())
        if (t) { targetPaneId = leaf.paneId; targetTabId = t.id; break }
      }
      if (!targetPaneId) { targetPaneId = leaves[0]?.paneId; targetTabId = leaves[0]?.activeTabId }
      if (!targetPaneId) return
      const groupId = msg.groupId ?? null
      updatePane(targetPaneId, p => ({
        tabs: p.tabs.map(t => t.id !== targetTabId ? t : groupId
          ? { ...t, groups: addSliderToGroupInTree(t.groups, groupId, slider) }
          : { ...t, sliders: [...t.sliders, slider] })
      }))
      postStateSnapshot()
    }

    if (msg.type === 'slider_name_update') {
      for (const leaf of allLeaves(get(layout))) {
        updatePane(leaf.paneId, p => ({
          tabs: p.tabs.map(t => ({
            ...t,
            sliders: t.sliders.map(s => s.id === msg.id ? { ...s, name: msg.name } : s),
            groups:  updateNameInGroupTree(t.groups, msg.id, msg.name)
          }))
        }))
      }
    }

    if (msg.type === 'cleared') {
      const leaves = allLeaves(get(layout))
      for (const leaf of leaves)
        updatePane(leaf.paneId, p => ({ tabs: p.tabs.map(t => ({ ...t, sliders: [], groups: [] })) }))
      postStateSnapshot()
    }

    if (msg.type === 'restore_state') {
      // msg.layout is the full layout tree (new format)
      // msg.tabs is the legacy flat format
      if (msg.layout) {
        restoreLayout(reconcileLayout(msg.layout))
      } else if (msg.tabs) {
        // legacy: single pane
        const leaf = makeLeaf(msg.tabs, msg.activeTabId)
        restoreLayout(leaf)
      }
      postStateSnapshot()
    }
  }

  // Walk a restored layout tree and hydrate it (re-attach live slider refs)
  // The tree already has values/min/max from C# RestoreState
  function reconcileLayout(node) {
    if (node.type === 'leaf') return node
    return { ...node, a: reconcileLayout(node.a), b: reconcileLayout(node.b) }
  }
</script>

<main class:edit={$mode === 'edit'}>
  <header class="global-toolbar">
    <EditToolbar
      bind:mode={$mode}
      bind:pinned={$pinned}
      on:pin={e => postToCs({ type: 'pin', value: e.detail })}
    />
  </header>

  <div class="layout-root">
    <PaneLayout node={$layout} />
  </div>
</main>

<style>
  :global(*, *::before, *::after) { box-sizing: border-box; margin: 0; padding: 0; }
  :global(body) {
    background: var(--bg);
    color: var(--text);
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
  main.edit { outline: 1px solid rgba(var(--accent-rgb), 0.2); }

  .global-toolbar {
    flex-shrink: 0;
    background: var(--panel-bg);
    border-bottom: 1px solid var(--bg);
  }

  .layout-root {
    flex: 1;
    overflow: hidden;
    display: flex;
  }
</style>
