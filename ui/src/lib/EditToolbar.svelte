<script>
  import { createEventDispatcher } from 'svelte'
  import { hoverHint, settingsOpen } from '../stores/uiState.js'
  const dispatch = createEventDispatcher()

  export let mode   = 'preview'
  export let pinned = false

  function toggleMode() { mode   = mode === 'edit' ? 'preview' : 'edit' }
  function togglePin()  { pinned = !pinned; dispatch('pin', pinned) }
</script>

<div class="toolbar">
  <div class="spacer"></div>

  <button class="mode-btn" class:active={mode === 'edit'} on:click={toggleMode}>
    <span class="dot" class:edit={mode === 'edit'} class:preview={mode !== 'edit'}></span>
    {mode === 'edit' ? 'Edit' : 'Preview'}
  </button>

  <button class="pin-btn" class:active={pinned} on:click={togglePin}
      on:mouseenter={() => hoverHint.set('Keep the window pinned on top of other apps')}
      on:mouseleave={() => hoverHint.set(null)}
      title={pinned ? 'Unpin' : 'Pin on top'}>
    <!-- Lucide "pin" icon (ISC license) — https://lucide.dev/icons/pin -->
    <svg width="12" height="12" viewBox="0 0 24 24" fill={pinned ? 'currentColor' : 'none'} stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <path d="M12 17v5" />
      <path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z" />
    </svg>
  </button>

  <button class="pin-btn" class:active={$settingsOpen} on:click={() => settingsOpen.update(v => !v)}
      on:mouseenter={() => hoverHint.set('Shortcuts & settings')}
      on:mouseleave={() => hoverHint.set(null)}
      title="Shortcuts & settings">
    <!-- Lucide "settings" icon (ISC license) — https://lucide.dev/icons/settings -->
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  </button>
</div>

<style>
  .toolbar {
    display: flex;
    align-items: center;
    flex-shrink: 0;
    gap: 5px;
    padding: 2px 8px;
    height: 24px;
  }

  .spacer { flex: 1; }

  .mode-btn {
    display: flex;
    align-items: center;
    gap: 5px;
    height: 20px;
    padding: 0 8px;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.58);
    font-size: 10px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .mode-btn:hover  { border-color: var(--border); color: rgba(var(--text-rgb), 0.73); }
  .mode-btn.active { border-color: rgba(var(--accent-rgb), 0.4); color: var(--accent-light); }

  .dot {
    width: 5px;
    height: 5px;
    border-radius: 50%;
    flex-shrink: 0;
  }
  .dot.edit    { background: var(--accent-light); }
  .dot.preview { background: rgba(var(--text-rgb), 0.36); }

.pin-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 20px;
    height: 20px;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.36);
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
    padding: 0;
    flex-shrink: 0;
  }
  .pin-btn:hover  { border-color: var(--border); color: rgba(var(--text-rgb), 0.7); }
  .pin-btn.active { border-color: rgba(var(--accent-rgb), 0.4); color: var(--accent-light); }
</style>
