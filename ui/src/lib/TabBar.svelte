<script>
  import { createEventDispatcher } from 'svelte'
  import { hoverHint } from '../stores/uiState.js'
  import { hex8ToRgba } from './colorUtils.js'
  import ContextMenu from './ContextMenu.svelte'
  import ColourPickerPopup from './ColourPickerPopup.svelte'
  const dispatch = createEventDispatcher()

  export let tabs        = []
  export let activeTabId = ''
  export let mode        = 'preview'
  export let isDragging  = false   // same-pane slider/group drag active
  export let dropTabId   = null
  export let canDragTabs = false   // cross-pane tab drag allowed
  export let crossPaneItemDrag = false   // a slider/group dragged FROM ANOTHER pane is in flight — lets a drop target this tab directly instead of always landing on whichever tab is active here

  let editingId = null
  let editValue = ''

  // ── rename ────────────────────────────────────────────────────────────────────
  function startRename(tab) {
    if (mode !== 'edit') return
    editingId = tab.id
    editValue = tab.label
  }
  function commitRename() {
    if (editingId && editValue.trim())
      dispatch('rename', { id: editingId, label: editValue.trim() })
    editingId = null
  }
  function onKeydown(e) {
    if (e.key === 'Enter')  { commitRename(); e.preventDefault() }
    if (e.key === 'Escape') { editingId = null }
  }
  function focusAndSelect(node) {
    node.focus()
    node.select()
  }

  // ── cross-pane tab drag ───────────────────────────────────────────────────────
  let dragTabId = null

  // Set from dragstart (a real drag actually starting), not pointerdown —
  // pointerdown fires on every plain click too, and a click never fires
  // dragend, so a version of this that flagged the tab on pointerdown left
  // it permanently dimmed (opacity: 0.4, the being-dragged look) the moment
  // you clicked it without dragging. dragstart/dragend always pair up, so
  // this can't get stuck.
  function onTabDragStart(e, tab) {
    if (!canDragTabs || mode !== 'edit') { e.preventDefault(); return }
    dragTabId = tab.id
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', tab.id)
    dispatch('tabDragStart', { tabId: tab.id, label: tab.label })
  }

  function onTabDragEnd() {
    dragTabId = null
    dispatch('tabDragEnd')
  }

  // ── per-tab colour (Obsidian-style folder colouring) ─────────────────────────
  // Previously built + removed 2026-08-27 (right-click opened the PANE's own
  // split/sort/close menu instead of a colour menu) — root cause confirmed
  // here, never fixed: the tab's own contextmenu handler called
  // preventDefault but not stopPropagation, so the event bubbled up to
  // Pane.svelte's own contextmenu listener on the surrounding .pane element,
  // which then built and showed ITS menu on top. See
  // yapilacaklar/tab-color-menu-bubbling.md.
  let colourMenu  = null   // { tabId, x, y, rawX, rawY } | null — small Change/Reset menu
  let colourPopup = null   // { tabId, x, y } | null — the actual HSLA/RGBA picker

  function onTabContextMenu(e, tab) {
    if (mode !== 'edit') return
    e.preventDefault()
    e.stopPropagation()
    const hasColour = !!tab.color
    const menuW = 150, menuH = hasColour ? 68 : 36
    colourMenu = {
      tabId: tab.id,
      x: Math.min(e.clientX, window.innerWidth  - menuW - 8),
      y: Math.min(e.clientY, window.innerHeight - menuH - 8),
      rawX: e.clientX, rawY: e.clientY,
    }
  }
  $: colourMenuItems = colourMenu ? [
    { label: 'Change Colour…', action: () => openColourPopup(colourMenu) },
    ...(tabs.find(t => t.id === colourMenu.tabId)?.color
      ? [{ label: 'Reset Colour', danger: true, action: () => dispatch('colorChange', { id: colourMenu.tabId, color: null }) }]
      : []),
  ] : []
  function openColourPopup({ tabId, rawX, rawY }) {
    const w = 216, h = 300
    colourPopup = {
      tabId,
      x: Math.min(rawX, window.innerWidth  - w - 8),
      y: Math.min(rawY, window.innerHeight - h - 8),
    }
  }
  function onColourChange(e) {
    if (colourPopup) dispatch('colorChange', { id: colourPopup.tabId, color: e.detail })
  }

  // rgba(...)-ready "r, g, b" triplet for a tab's stored hex8 colour — used
  // for both the active tab's own pill (replacing the fixed accent tint) and
  // the whole tabbar's wash below.
  function tabRgb(color) {
    const { r, g, b } = hex8ToRgba(color)
    return `${r}, ${g}, ${b}`
  }

  $: activeTab = tabs.find(t => t.id === activeTabId)
  // Wash on the bar itself, not just the active tab's own pill — low opacity
  // since it spans the whole header width, not a small chip.
  $: barStyle = activeTab?.color ? `background: rgba(${tabRgb(activeTab.color)}, 0.08);` : ''
</script>

<div class="tabbar" style={barStyle}>
  {#each tabs as tab (tab.id)}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <div
      class="tab"
      class:active={tab.id === activeTabId}
      class:drag-target={(isDragging || crossPaneItemDrag) && tab.id !== activeTabId}
      class:drag-over={dropTabId === tab.id}
      class:being-dragged={dragTabId === tab.id}
      draggable={canDragTabs && mode === 'edit' && editingId !== tab.id}
      style={tab.id === activeTabId && tab.color
        ? `background: rgba(${tabRgb(tab.color)}, 0.22); border-color: rgba(${tabRgb(tab.color)}, 0.45); color: var(--text);`
        : ''}
      on:click={() => dispatch('select', tab.id)}
      on:dblclick={() => startRename(tab)}
      on:contextmenu={e => onTabContextMenu(e, tab)}
      on:mouseenter={() => mode === 'edit' && hoverHint.set('Double-click: rename  ·  Right-click: colour  ·  Drag: reorder or move to another pane')}
      on:mouseleave={() => hoverHint.set(null)}
      on:dragstart={e => onTabDragStart(e, tab)}
      on:dragend={onTabDragEnd}
      on:dragover|preventDefault={() => crossPaneItemDrag ? dispatch('itemDragOverTab', tab.id) : (isDragging && dispatch('tabDragOver', tab.id))}
      on:dragleave={e => { if (!e.currentTarget.contains(e.relatedTarget)) dispatch(crossPaneItemDrag ? 'itemDragLeaveTab' : 'tabDragLeave', tab.id) }}
      on:drop|preventDefault={() => dispatch(crossPaneItemDrag ? 'itemDropOnTab' : 'tabDrop', tab.id)}
      role="tab"
      tabindex="0"
      on:keydown={e => e.key === 'Enter' && dispatch('select', tab.id)}
    >
      {#if editingId === tab.id}
        <input
          class="rename-input"
          bind:value={editValue}
          use:focusAndSelect
          on:blur={commitRename}
          on:keydown={onKeydown}
          on:click|stopPropagation
        />
      {:else}
        {#if tab.color}<span class="dot" style="background: {tab.color}"></span>{/if}
        <span class="label">{tab.label}</span>
        {#if mode === 'edit' && tabs.length > 1}
          <button
            class="remove"
            on:click|stopPropagation={() => dispatch('remove', tab.id)}
            title="Remove tab"
          >×</button>
        {/if}
      {/if}
    </div>
  {/each}

  {#if mode === 'edit'}
    <button class="add-tab" on:click={() => dispatch('add')} title="Add tab"
        on:mouseenter={() => hoverHint.set('New tab')}
        on:mouseleave={() => hoverHint.set(null)}>+</button>
    <div class="sep"></div>
    <button class="capture-btn" on:click={() => dispatch('capture')} title="Capture selected sliders"
        on:mouseenter={() => hoverHint.set('Capture selected sliders into a new tab')}
        on:mouseleave={() => hoverHint.set(null)}>
      + Capture
    </button>
  {/if}
</div>

{#if colourMenu}
  <ContextMenu x={colourMenu.x} y={colourMenu.y} items={colourMenuItems} on:close={() => colourMenu = null} />
{/if}

{#if colourPopup}
  <ColourPickerPopup x={colourPopup.x} y={colourPopup.y}
    hex={tabs.find(t => t.id === colourPopup.tabId)?.color ?? '#74a2ffff'}
    on:change={onColourChange}
    on:close={() => colourPopup = null} />
{/if}

<style>
  .tabbar {
    display: flex;
    align-items: center;
    height: 20px;
    gap: 3px;
    padding: 0 10px;
    overflow-x: auto;
    scrollbar-width: none;
  }
  .tabbar::-webkit-scrollbar { display: none; }

  .tab {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 0 8px;
    height: 14px;
    cursor: pointer;
    border-radius: 3px;
    border: 1px solid transparent;
    background: rgba(var(--text-rgb), 0.045);
    color: rgba(var(--text-rgb), 0.36);
    font-size: 10px;
    white-space: nowrap;
    transition: color 0.15s, border-color 0.15s, background 0.1s;
    flex-shrink: 0;
    user-select: none;
  }
  .tab:hover              { color: rgba(var(--text-rgb), 0.65); background: rgba(var(--text-rgb), 0.08); }
  /* Active uses the accent hue (not another shade of the same grey wash
     hover/inactive already use) — same "this is the selected one" language
     as .row.selected elsewhere in the app. Grey-on-grey here was too subtle
     to reliably read at a glance, especially with several panes' tab bars
     on screen at once, each easy to mistake as "no active tab visible". */
  .tab.active              { color: var(--text); background: rgba(var(--accent-rgb), 0.18); border-color: rgba(var(--accent-rgb), 0.35); }
  .tab.drag-target        { color: rgba(var(--text-rgb), 0.43); }
  .tab.drag-over          { background: rgba(var(--accent-rgb), 0.15); color: var(--accent-light); border-color: rgba(var(--accent-rgb), 0.53); }
  .tab.being-dragged      { opacity: 0.4; }

  .label { pointer-events: none; }
  .dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    flex-shrink: 0;
    pointer-events: none;
  }

  .remove {
    display: flex; align-items: center; justify-content: center;
    width: 10px; height: 10px;
    border: none; background: transparent;
    color: rgba(var(--text-rgb), 0.29); font-size: 10px; line-height: 1;
    cursor: pointer; border-radius: 3px;
    transition: background 0.1s, color 0.1s;
    padding: 0;
  }
  .remove:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.65); }

  .add-tab {
    display: flex; align-items: center; justify-content: center;
    width: 14px; height: 14px;
    border: none; background: transparent;
    border-radius: 3px;
    color: rgba(var(--text-rgb), 0.29); font-size: 11px;
    cursor: pointer; flex-shrink: 0;
    transition: color 0.1s, background 0.1s;
    padding: 0;
  }
  .add-tab:hover { color: rgba(var(--text-rgb), 0.58); background: rgba(var(--text-rgb), 0.08); }

  .sep { width: 1px; height: 10px; background: var(--grid); margin: 0 2px; flex-shrink: 0; }

  .capture-btn {
    height: 14px;
    align-self: center;
    padding: 0 6px;
    border: 1px solid rgba(var(--accent-rgb), 0.27);
    border-radius: 3px;
    background: transparent;
    color: var(--accent-light);
    font-size: 9px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
    flex-shrink: 0;
    white-space: nowrap;
  }
  .capture-btn:hover { border-color: rgba(var(--accent-rgb), 0.6); color: var(--accent); }

  .rename-input {
    background: var(--panel-bg);
    border: 1px solid rgba(var(--accent-rgb), 0.53);
    border-radius: 3px;
    color: var(--text);
    font-size: 12px;
    font-family: inherit;
    padding: 1px 5px;
    width: 90px;
    outline: none;
  }
</style>
