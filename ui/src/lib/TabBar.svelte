<script>
  import { createEventDispatcher } from 'svelte'
  import { hoverHint } from '../stores/uiState.js'
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
</script>

<div class="tabbar">
  {#each tabs as tab (tab.id)}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <div
      class="tab"
      class:active={tab.id === activeTabId}
      class:drag-target={(isDragging || crossPaneItemDrag) && tab.id !== activeTabId}
      class:drag-over={dropTabId === tab.id}
      class:being-dragged={dragTabId === tab.id}
      draggable={canDragTabs && mode === 'edit' && editingId !== tab.id}
      on:click={() => dispatch('select', tab.id)}
      on:dblclick={() => startRename(tab)}
      on:mouseenter={() => mode === 'edit' && hoverHint.set('Double-click: rename  ·  Drag: reorder or move to another pane')}
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
