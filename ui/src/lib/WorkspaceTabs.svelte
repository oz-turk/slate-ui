<script>
  import { workspaces, activeWorkspaceId, addWorkspace, removeWorkspace, renameWorkspace, setActiveWorkspace, reorderWorkspaces } from '../stores/layout.js'
  import { postStateSnapshot } from './ipc.js'
  import { hoverHint } from '../stores/uiState.js'
  import { tabDrag, itemDrag } from '../stores/dragState.js'

  export let mode = 'preview'

  let editingId = null
  let editValue = ''

  function startRename(w) {
    if (mode !== 'edit') return
    editingId = w.id
    editValue = w.label
  }
  function commitRename() {
    if (editingId && editValue.trim()) {
      renameWorkspace(editingId, editValue.trim())
      postStateSnapshot()
    }
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

  function activate(id) {
    setActiveWorkspace(id)
    postStateSnapshot()
  }

  // Switching workspace mid-drag — a tab/slider/group drag is a native HTML5
  // DnD (see dragState.js), which the browser tracks independently of the
  // DOM underneath the cursor, so swapping the active workspace here doesn't
  // drop it. Only fires once per hover-in (guarded by the id check), so it
  // doesn't spam activate()/postStateSnapshot() on every dragover frame.
  function onDragEnterTab(id) {
    if (!$tabDrag && !$itemDrag) return
    if (id !== $activeWorkspaceId) activate(id)
  }
  // ── workspace reorder ────────────────────────────────────────────────────────
  // Separate from tabDrag/itemDrag on purpose: those stores drive the
  // "hover a workspace to switch to it" behaviour above, which must not
  // fire while the workspace tabs themselves are being dragged.
  let dragWsId = null
  let overWsId = null
  let overSide = 'before'

  function onWsDragStart(e, w) {
    if (mode !== 'edit' || editingId === w.id) { e.preventDefault(); return }
    dragWsId = w.id
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', w.id)
  }
  function onWsDragEnd() { dragWsId = null; overWsId = null }
  function onWsDrop(w) {
    if (!dragWsId) return
    if (w.id !== dragWsId) {
      reorderWorkspaces(dragWsId, w.id)
      postStateSnapshot()
    }
    dragWsId = null
    overWsId = null
  }
  function remove(id) {
    removeWorkspace(id)
    postStateSnapshot()
  }
  function add() {
    addWorkspace()
    postStateSnapshot()
  }
</script>

<div class="workspace-tabs">
  {#each $workspaces as w (w.id)}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <div class="ws-tab" class:active={w.id === $activeWorkspaceId}
        class:being-dragged={dragWsId === w.id}
        class:ins-before={overWsId === w.id && overSide === 'before'}
        class:ins-after={overWsId === w.id && overSide === 'after'}
        draggable={mode === 'edit' && editingId !== w.id}
        on:dragstart={e => onWsDragStart(e, w)}
        on:dragend={onWsDragEnd}
        on:drop|preventDefault={() => onWsDrop(w)}
        role="tab"
        tabindex="0"
        on:click={() => activate(w.id)}
        on:dblclick={() => startRename(w)}
        on:keydown={e => e.key === 'Enter' && activate(w.id)}
        on:mouseenter={() => hoverHint.set(mode === 'edit' ? 'Double-click: rename  ·  Ctrl+1–9: switch workspace' : 'Ctrl+1–9: switch workspace')}
        on:mouseleave={() => hoverHint.set(null)}
        on:dragenter|preventDefault={() => onDragEnterTab(w.id)}
        on:dragover|preventDefault={() => {
          if (!dragWsId) return
          overWsId = w.id !== dragWsId ? w.id : null
          const list = $workspaces
          overSide = list.findIndex(x => x.id === dragWsId) > list.findIndex(x => x.id === w.id) ? 'before' : 'after'
        }}
        on:dragleave={e => { if (!e.currentTarget.contains(e.relatedTarget) && overWsId === w.id) overWsId = null }}
    >
      {#if editingId === w.id}
        <input class="rename-input" bind:value={editValue} use:focusAndSelect
          on:blur={commitRename} on:keydown={onKeydown} on:click|stopPropagation />
      {:else}
        <span class="label">{w.label}</span>
        {#if mode === 'edit' && $workspaces.length > 1}
          <button class="remove" on:click|stopPropagation={() => remove(w.id)} title="Remove workspace">×</button>
        {/if}
      {/if}
    </div>
  {/each}

  {#if mode === 'edit'}
    <button class="add-ws" on:click={add} title="Add workspace"
        on:mouseenter={() => hoverHint.set('New workspace')}
        on:mouseleave={() => hoverHint.set(null)}>+</button>
  {/if}
</div>

<style>
  .workspace-tabs {
    display: flex;
    align-items: center;
    height: 24px;
    gap: 3px;
    padding: 0 8px;
    overflow-x: auto;
    scrollbar-width: none;
    flex: 1;
    min-width: 0;
  }
  .workspace-tabs::-webkit-scrollbar { display: none; }

  .ws-tab {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 0 9px;
    height: 18px;
    cursor: pointer;
    border-radius: 3px;
    background: var(--ws-tab-bg, rgba(0, 0, 0, 0.15));
    color: rgba(var(--text-rgb), 0.36);
    font-size: 11px;
    font-weight: 600;
    white-space: nowrap;
    transition: color 0.15s, background 0.1s;
    flex-shrink: 0;
    user-select: none;
  }
  .ws-tab:hover  { color: rgba(var(--text-rgb), 0.65); background: var(--ws-tab-bg-hover, rgba(0, 0, 0, 0.25)); }
  .ws-tab.active { color: var(--text); background: var(--ws-tab-bg-active, rgba(255, 255, 255, 0.14)); }

  .ws-tab.being-dragged { opacity: 0.4; }
  .ws-tab { position: relative; }
  /* Reorder insertion marker: vertical accent line in the gap beside the hovered tab */
  .ws-tab.ins-before::before,
  .ws-tab.ins-after::after {
    content: '';
    position: absolute;
    top: -3px; bottom: -3px;
    width: 2px;
    border-radius: 1px;
    background: var(--accent);
    box-shadow: 0 0 4px rgba(var(--accent-rgb), 0.8);
    pointer-events: none;
  }
  .ws-tab.ins-before::before { left: -3px; }
  .ws-tab.ins-after::after   { right: -3px; }

  .label { pointer-events: none; }

  .remove {
    display: flex; align-items: center; justify-content: center;
    width: 13px; height: 13px;
    border: none; background: transparent;
    color: rgba(var(--text-rgb), 0.29); font-size: 12px; line-height: 1;
    cursor: pointer; border-radius: 3px;
    transition: background 0.1s, color 0.1s;
    padding: 0;
  }
  .remove:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.65); }

  .add-ws {
    display: flex; align-items: center; justify-content: center;
    width: 18px; height: 18px;
    border: none; background: transparent;
    border-radius: 3px;
    color: rgba(var(--text-rgb), 0.29); font-size: 13px;
    cursor: pointer; flex-shrink: 0;
    transition: color 0.1s, background 0.1s;
    padding: 0;
  }
  .add-ws:hover { color: rgba(var(--text-rgb), 0.58); background: rgba(var(--text-rgb), 0.08); }

  .rename-input {
    background: var(--panel-bg);
    border: 1px solid rgba(var(--accent-rgb), 0.53);
    border-radius: 3px;
    color: var(--text);
    font-size: 11px;
    font-family: inherit;
    padding: 1px 5px;
    width: 90px;
    outline: none;
  }
</style>
