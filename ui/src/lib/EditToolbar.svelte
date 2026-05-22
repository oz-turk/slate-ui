<script>
  import { createEventDispatcher } from 'svelte'
  const dispatch = createEventDispatcher()

  export let mode   = 'preview'
  export let pinned = false

  function toggleMode() { mode   = mode === 'edit' ? 'preview' : 'edit' }
  function togglePin()  { pinned = !pinned; dispatch('pin', pinned) }
</script>

<div class="toolbar">
  <button class="mode-btn" class:active={mode === 'edit'} on:click={toggleMode}>
    <span class="dot" class:edit={mode === 'edit'} class:preview={mode !== 'edit'}></span>
    {mode === 'edit' ? 'Edit' : 'Preview'}
  </button>

  <div class="spacer"></div>

  <button class="pin-btn" class:active={pinned} on:click={togglePin} title={pinned ? 'Unpin' : 'Pin on top'}>
    <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
      {#if pinned}
        <path d="M6 1 L8 4 L11 4 L8 7 L8 10 L6 8 L4 10 L4 7 L1 4 L4 4 Z" fill="currentColor"/>
      {:else}
        <path d="M6 1 L8 4 L11 4 L8 7 L8 10 L6 8 L4 10 L4 7 L1 4 L4 4 Z" stroke="currentColor" stroke-width="1" fill="none"/>
      {/if}
    </svg>
  </button>
</div>

<style>
  .toolbar {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 6px 10px;
    height: 34px;
  }

  .spacer { flex: 1; }

  .mode-btn {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 3px 10px;
    border: 1px solid #2e2e2e;
    border-radius: 4px;
    background: transparent;
    color: #888;
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .mode-btn:hover  { border-color: #444; color: #bbb; }
  .mode-btn.active { border-color: #3b7fff66; color: #6fa3ff; }

  .dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    flex-shrink: 0;
  }
  .dot.edit    { background: #6fa3ff; }
  .dot.preview { background: #555; }

.pin-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 24px;
    height: 24px;
    border: 1px solid #2e2e2e;
    border-radius: 4px;
    background: transparent;
    color: #555;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
    padding: 0;
    flex-shrink: 0;
  }
  .pin-btn:hover  { border-color: #444; color: #aaa; }
  .pin-btn.active { border-color: #3b7fff66; color: #6fa3ff; }
</style>
