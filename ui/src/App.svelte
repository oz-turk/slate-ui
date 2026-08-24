<script>
  import { onMount }    from 'svelte'
  import { get }        from 'svelte/store'
  import PaneLayout     from './lib/PaneLayout.svelte'
  import EditToolbar    from './lib/EditToolbar.svelte'
  import WorkspaceTabs  from './lib/WorkspaceTabs.svelte'
  import StatusBar      from './lib/StatusBar.svelte'
  import SettingsPanel  from './lib/SettingsPanel.svelte'
  import {
    layout, workspaces, activeWorkspaceId, allLeaves, restoreLayout, restoreWorkspaces,
    updatePane, syncControl, clearAllWorkspaces, resetToDefault, makeLeaf, setActiveWorkspace
  } from './stores/layout.js'
  import { mode, pinned, theme, deleteRequest, captureRequest, settingsOpen, clearSelectionTick, hoverHint } from './stores/uiState.js'
  import { undo, suppressDuring } from './stores/history.js'
  import { postToCs, postStateSnapshot } from './lib/ipc.js'

  // Driven from the theme store directly rather than var(--panel-bg) — that
  // var kept resolving to something dark in light mode for this specific
  // consumer across several rebuilds (root cause never pinned down; the same
  // JS-driven approach for Pane.svelte's edge-tint border was confirmed
  // working via a lime/magenta diagnostic build, so applying it here too).
  $: toolbarBg = $theme === 'light' ? '#f7f5f0' : '#303030'

  // app.css's light-mode override lives on :root[data-theme="light"] (not
  // main[data-theme] — see app.css's comment: custom properties don't
  // inherit upward from <main> to <body>/<html>), so <html> itself needs
  // the attribute too, not just the <main data-theme> below.
  $: document.documentElement.dataset.theme = $theme

  // ── C# ↔ JS ───────────────────────────────────────────────────────────────────
  onMount(() => {
    window.chrome?.webview?.addEventListener('message', e => {
      try { handleMessage(JSON.parse(e.data)) } catch {}
    })
    postToCs({ type: 'ui_ready' })
  })

  // ── Global keyboard shortcuts ────────────────────────────────────────────────
  // Ignored while typing in a text field (renaming a tab/group, editing a value)
  // so keys like x/c/Tab still behave normally there instead of being hijacked.
  function isTextEditable(el) {
    return el?.tagName === 'INPUT' || el?.tagName === 'TEXTAREA' || el?.isContentEditable
  }

  // Tracked continuously so x/c can resolve "whatever's under the mouse" without
  // needing a real pointer event at keypress time.
  let mouseX = 0, mouseY = 0
  onMount(() => {
    function onPointerMove(e) { mouseX = e.clientX; mouseY = e.clientY }
    window.addEventListener('pointermove', onPointerMove)
    return () => window.removeEventListener('pointermove', onPointerMove)
  })

  onMount(() => {
    function onKeydown(e) {
      if (isTextEditable(document.activeElement)) return

      if (e.key === 'Tab') {
        e.preventDefault()
        mode.update(m => m === 'edit' ? 'preview' : 'edit')
        return
      }

      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        e.preventDefault()
        undo()
        return
      }

      if ((e.ctrlKey || e.metaKey) && e.key >= '1' && e.key <= '9') {
        const idx = parseInt(e.key, 10) - 1
        const ws = get(workspaces)[idx]
        if (ws) { e.preventDefault(); setActiveWorkspace(ws.id) }
        return
      }

      if (get(mode) !== 'edit') return

      if (e.key === 'Escape') {
        clearSelectionTick.update(n => n + 1)
        return
      }

      if (e.key === 'x') {
        const el = document.elementFromPoint(mouseX, mouseY)
        const sliderEl = el?.closest('[data-slider-id]')
        const paneEl   = el?.closest('[data-pane-id]')
        if (sliderEl && paneEl) deleteRequest.set({ paneId: paneEl.dataset.paneId, sliderId: sliderEl.dataset.sliderId })
        return
      }

      if (e.key === 'c') {
        const el = document.elementFromPoint(mouseX, mouseY)
        const paneEl = el?.closest('[data-pane-id]')
        if (paneEl) captureRequest.set(paneEl.dataset.paneId)
        return
      }
    }
    window.addEventListener('keydown', onKeydown)
    return () => window.removeEventListener('keydown', onKeydown)
  })

  // Spans every workspace, not just the active one — a GH object already
  // captured somewhere shouldn't silently get captured again into another.
  function allSliderIds() {
    const ids = new Set()
    function collectGroups(groups) {
      for (const g of groups) {
        g.sliders.forEach(s => ids.add(s.id))
        collectGroups(g.groups ?? [])
      }
    }
    for (const w of get(workspaces))
      for (const leaf of allLeaves(w.layout))
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

  // Shared by every "captured a control" message (slider_added, toggle_added, ...) —
  // finds where it should land (by tabId/label, falling back to the first pane/tab)
  // and inserts it into that tab's top-level list or the target group.
  function addCapturedControl(control, msg) {
    if (allSliderIds().has(control.id)) return
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
        ? { ...t, groups: addSliderToGroupInTree(t.groups, groupId, control) }
        : { ...t, sliders: [...t.sliders, control] })
    }))
    postStateSnapshot()
  }

  // WebView2's own Ctrl+scroll zoom — C# posts the new factor as it changes;
  // shown as a transient status-bar hint (same slot hover hints use) since
  // there's no hover/leave pair to key off of, just cleared a beat after the
  // last event so a burst of wheel ticks doesn't flicker it.
  let zoomHideTimer = null
  function showZoomHint(factor) {
    hoverHint.set(`Zoom: ${Math.round(factor * 100)}%`)
    clearTimeout(zoomHideTimer)
    zoomHideTimer = setTimeout(() => hoverHint.set(null), 900)
  }

  function handleMessage(msg) {
    if (msg.type === 'zoom_changed') {
      showZoomHint(msg.factor)
    }

    if (msg.type === 'slider_added') {
      addCapturedControl({ id: msg.id, type: 'slider', name: msg.name, min: msg.min, max: msg.max, value: msg.value }, msg)
    }

    if (msg.type === 'toggle_added') {
      addCapturedControl({ id: msg.id, type: 'toggle', name: msg.name, value: msg.value }, msg)
    }

    if (msg.type === 'button_added') {
      addCapturedControl({ id: msg.id, type: 'button', name: msg.name, value: msg.value }, msg)
    }

    if (msg.type === 'valueList_added') {
      addCapturedControl({ id: msg.id, type: 'valueList', name: msg.name, options: msg.options, value: msg.value, multiSelect: msg.multiSelect, cycle: msg.cycle, loop: msg.loop }, msg)
    }

    if (msg.type === 'panel_added') {
      addCapturedControl({ id: msg.id, type: 'panel', name: msg.name, value: msg.value, readOnly: msg.readOnly, height: 88 }, msg)
    }

    if (msg.type === 'itemPicker_added') {
      addCapturedControl({ id: msg.id, type: 'itemPicker', name: msg.name, options: msg.options, value: msg.value }, msg)
    }

    // Human plugin's "Item Selector" — same shape as valueList/itemPicker on the
    // wire, kept as its own type so it round-trips through RestoreState correctly.
    if (msg.type === 'humanValueList_added') {
      addCapturedControl({ id: msg.id, type: 'humanValueList', name: msg.name, options: msg.options, value: msg.value, multiSelect: msg.multiSelect, cycle: msg.cycle, loop: msg.loop }, msg)
    }

    if (msg.type === 'colourPicker_added') {
      addCapturedControl({ id: msg.id, type: 'colourPicker', name: msg.name, value: msg.value }, msg)
    }

    // Pancake plugin's "True Only Button" — same shape as a core button on the
    // wire, kept as its own type so it round-trips through RestoreState correctly.
    if (msg.type === 'pancakeButton_added') {
      addCapturedControl({ id: msg.id, type: 'pancakeButton', name: msg.name, value: msg.value }, msg)
    }

    if (msg.type === 'slider_name_update') {
      syncControl(msg.id, { name: msg.name })
    }

    // Panels, item pickers and Item Selectors can change on their own between
    // solves (panel: upstream data flowing into a connected/read-only one; the
    // other two: their wired candidate list) — synced separately since they
    // carry more than a name.
    if (msg.type === 'panel_text_update') {
      syncControl(msg.id, { name: msg.name, value: msg.text, readOnly: msg.readOnly })
    }

    if (msg.type === 'itemPicker_update') {
      syncControl(msg.id, { name: msg.name, options: msg.options, value: msg.value })
    }

    if (msg.type === 'humanValueList_update') {
      syncControl(msg.id, { name: msg.name, options: msg.options, value: msg.value, multiSelect: msg.multiSelect, cycle: msg.cycle, loop: msg.loop })
    }

    if (msg.type === 'colourPicker_update') {
      syncControl(msg.id, { name: msg.name, value: msg.value })
    }

    if (msg.type === 'cleared') {
      clearAllWorkspaces()
      postStateSnapshot()
    }

    if (msg.type === 'reset') {
      resetToDefault()
      postStateSnapshot()
    }

    if (msg.type === 'restore_state') {
      // msg.workspaces is the current multi-workspace format
      // msg.layout is the older single-tree format
      // msg.tabs is the oldest, legacy flat format
      // Loading a file is not a user action — suppress it so it doesn't land
      // on the undo stack (undoing past it would blow away the loaded layout).
      const winW = msg.winW ?? 900, winH = msg.winH ?? 600
      suppressDuring(() => {
        if (msg.workspaces) {
          restoreWorkspaces(
            msg.workspaces.map(w => ({ ...w, layout: reconcileLayout(w.layout, winW, winH) })),
            msg.activeWorkspaceId
          )
        } else if (msg.layout) {
          restoreLayout(reconcileLayout(msg.layout, winW, winH))
        } else if (msg.tabs) {
          // legacy: single pane
          const leaf = makeLeaf(msg.tabs, msg.activeTabId)
          restoreLayout(leaf)
        }
      })
      if (msg.theme) theme.set(msg.theme)
      postStateSnapshot()
    }
  }

  // Walk a restored layout tree and hydrate it (re-attach live slider refs).
  // The tree already has values/min/max from C# RestoreState.
  //
  // Also migrates pre-sizeA saves: those split nodes only have a fractional
  // "ratio" (0-1), not a pixel sizeA — converted here using (w, h), the pixel
  // space actually available to this node. That starts as the whole window
  // (winW/winH, sent alongside restore_state — the window size the ratio was
  // set against) and shrinks going down the tree as ancestor splits carve it
  // up, so each split's ratio is read against the space it really had.
  function reconcileLayout(node, w, h) {
    if (node.type === 'leaf') return node
    let sizeA = node.sizeA ?? (node.ratio != null ? Math.round(node.ratio * (node.dir === 'h' ? w : h)) : 260)
    const aw = node.dir === 'h' ? sizeA : w
    const ah = node.dir === 'h' ? h : sizeA
    const bw = node.dir === 'h' ? Math.max(0, w - sizeA) : w
    const bh = node.dir === 'h' ? h : Math.max(0, h - sizeA)
    return { ...node, sizeA, a: reconcileLayout(node.a, aw, ah), b: reconcileLayout(node.b, bw, bh) }
  }
</script>

<main class:edit={$mode === 'edit'} data-theme={$theme}>
  <header class="global-toolbar" style="background: {toolbarBg}">
    <WorkspaceTabs mode={$mode} />
    <EditToolbar
      bind:mode={$mode}
      bind:pinned={$pinned}
      on:pin={e => postToCs({ type: 'pin', value: e.detail })}
    />
  </header>
  {#if $settingsOpen}<SettingsPanel />{/if}

  <div class="layout-root">
    <PaneLayout node={$layout} />
  </div>

  <StatusBar />
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
    position: relative;
  }
  main.edit { outline: 1px solid rgba(var(--accent-rgb), 0.2); }

  .global-toolbar {
    flex-shrink: 0;
    display: flex;
    align-items: stretch;
    /* background set inline from toolbarBg (JS/$theme-driven, see above) */
    border-radius: 0 0 4px 4px;
    overflow: hidden;
    margin-bottom: 3px;
  }

  .layout-root {
    flex: 1;
    overflow: hidden;
    display: flex;
    padding: 0 3px 3px 3px;
  }
</style>
