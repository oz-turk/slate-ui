<script>
  import { workspaces, activeWorkspaceId, addWorkspace, removeWorkspace, renameWorkspace, setActiveWorkspace } from '../stores/layout.js'
  import { postStateSnapshot } from './ipc.js'

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

  function activate(id) {
    setActiveWorkspace(id)
    postStateSnapshot()
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
        role="tab"
        tabindex="0"
        on:click={() => activate(w.id)}
        on:dblclick={() => startRename(w)}
        on:keydown={e => e.key === 'Enter' && activate(w.id)}
    >
      {#if editingId === w.id}
        <!-- svelte-ignore a11y-autofocus -->
        <input class="rename-input" bind:value={editValue} autofocus
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
    <button class="add-ws" on:click={add} title="Add workspace">+</button>
  {/if}
</div>

<style>
  .workspace-tabs {
    display: flex;
    align-items: stretch;
    gap: 1px;
    padding: 0 4px;
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
    padding: 0 10px;
    height: 24px;
    cursor: pointer;
    border-bottom: 2px solid transparent;
    color: rgba(var(--text-rgb), 0.36);
    font-size: 11px;
    font-weight: 600;
    white-space: nowrap;
    transition: color 0.15s, border-color 0.15s;
    flex-shrink: 0;
    user-select: none;
  }
  .ws-tab:hover  { color: rgba(var(--text-rgb), 0.65); }
  .ws-tab.active { color: var(--text); border-bottom-color: var(--accent); }

  .label { pointer-events: none; }

  .remove {
    display: flex; align-items: center; justify-content: center;
    width: 14px; height: 14px;
    border: none; background: transparent;
    color: rgba(var(--text-rgb), 0.29); font-size: 13px; line-height: 1;
    cursor: pointer; border-radius: 3px;
    transition: background 0.1s, color 0.1s;
    padding: 0;
  }
  .remove:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.65); }

  .add-ws {
    display: flex; align-items: center; justify-content: center;
    width: 20px; height: 24px;
    border: none; background: transparent;
    color: rgba(var(--text-rgb), 0.29); font-size: 14px;
    cursor: pointer; flex-shrink: 0;
    transition: color 0.1s;
    padding: 0;
    align-self: center;
  }
  .add-ws:hover { color: rgba(var(--text-rgb), 0.58); }

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
