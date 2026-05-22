<script>
  import { createEventDispatcher } from 'svelte'
  const dispatch = createEventDispatcher()

  export let tabs        = []
  export let activeTabId = ''
  export let mode        = 'preview'
  export let isDragging  = false   // slider/group drag active
  export let dropTabId   = null
  export let canDragTabs = false   // cross-pane tab drag allowed

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

  // ── cross-pane tab drag ───────────────────────────────────────────────────────
  let dragTabId = null

  function onTabPointerDown(e, tab) {
    if (!canDragTabs || mode !== 'edit') return
    dragTabId = tab.id
  }

  function onTabDragStart(e, tab) {
    if (!canDragTabs || mode !== 'edit') { e.preventDefault(); return }
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
      class:drag-target={isDragging && tab.id !== activeTabId}
      class:drag-over={dropTabId === tab.id}
      class:being-dragged={dragTabId === tab.id}
      draggable={canDragTabs && mode === 'edit'}
      on:click={() => dispatch('select', tab.id)}
      on:dblclick={() => startRename(tab)}
      on:pointerdown={e => onTabPointerDown(e, tab)}
      on:dragstart={e => onTabDragStart(e, tab)}
      on:dragend={onTabDragEnd}
      on:dragover|preventDefault={() => isDragging && dispatch('tabDragOver', tab.id)}
      on:dragleave={e => { if (!e.currentTarget.contains(e.relatedTarget)) dispatch('tabDragLeave', tab.id) }}
      on:drop|preventDefault={() => dispatch('tabDrop', tab.id)}
      role="tab"
      tabindex="0"
      on:keydown={e => e.key === 'Enter' && dispatch('select', tab.id)}
    >
      {#if editingId === tab.id}
        <!-- svelte-ignore a11y-autofocus -->
        <input
          class="rename-input"
          bind:value={editValue}
          autofocus
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
    <button class="add-tab" on:click={() => dispatch('add')} title="Add tab">+</button>
    <div class="sep"></div>
    <button class="capture-btn" on:click={() => dispatch('capture')} title="Capture selected sliders">
      + Capture
    </button>
  {/if}
</div>

<style>
  .tabbar {
    display: flex;
    align-items: stretch;
    gap: 1px;
    padding: 0 8px;
    overflow-x: auto;
    scrollbar-width: none;
  }
  .tabbar::-webkit-scrollbar { display: none; }

  .tab {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 0 12px;
    height: 32px;
    cursor: pointer;
    border-bottom: 2px solid transparent;
    color: #555;
    font-size: 12px;
    white-space: nowrap;
    transition: color 0.15s, border-color 0.15s, background 0.1s;
    flex-shrink: 0;
    user-select: none;
  }
  .tab:hover              { color: #999; }
  .tab.active             { color: #e2e2e2; border-bottom-color: #3b7fff; }
  .tab.drag-target        { color: #666; }
  .tab.drag-over          { background: #1a2540; color: #8ab4ff; border-bottom-color: #3b7fff88; }
  .tab.being-dragged      { opacity: 0.4; }

  .label { pointer-events: none; }

  .remove {
    display: flex; align-items: center; justify-content: center;
    width: 14px; height: 14px;
    border: none; background: transparent;
    color: #444; font-size: 13px; line-height: 1;
    cursor: pointer; border-radius: 3px;
    transition: background 0.1s, color 0.1s;
    padding: 0;
  }
  .remove:hover { background: #2e2e2e; color: #999; }

  .add-tab {
    display: flex; align-items: center; justify-content: center;
    width: 24px; height: 32px;
    border: none; background: transparent;
    color: #444; font-size: 16px;
    cursor: pointer; flex-shrink: 0;
    transition: color 0.1s;
    padding: 0;
  }
  .add-tab:hover { color: #888; }

  .sep { width: 1px; height: 16px; background: #2a2a2a; margin: 0 2px; flex-shrink: 0; }

  .capture-btn {
    padding: 3px 8px;
    border: 1px solid #3b7fff44;
    border-radius: 4px;
    background: transparent;
    color: #6fa3ff88;
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
    flex-shrink: 0;
    white-space: nowrap;
  }
  .capture-btn:hover { border-color: #3b7fff99; color: #6fa3ff; }

  .rename-input {
    background: #1e1e1e;
    border: 1px solid #3b7fff88;
    border-radius: 3px;
    color: #e2e2e2;
    font-size: 12px;
    font-family: inherit;
    padding: 1px 5px;
    width: 90px;
    outline: none;
  }
</style>
