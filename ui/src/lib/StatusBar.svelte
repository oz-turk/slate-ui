<script>
  import { mode, hoverHint } from '../stores/uiState.js'

  export const VERSION = '0.1.4'
</script>

<div class="status-bar">
  <div class="hint">
    {#if $hoverHint}
      <span class="hint-item">{$hoverHint}</span>
    {:else if $mode === 'edit'}
      <span class="hint-item">
        <span class="kbd">
          <!-- mouse-left: composite icon, body shape based on Lucide "mouse" (ISC) with a filled left-button highlight — see NOTICE.md -->
          <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="5" y="2" width="14" height="20" rx="7" />
            <line x1="12" y1="2" x2="12" y2="8" />
            <line x1="5" y1="8" x2="19" y2="8" />
            <rect x="6" y="3" width="6" height="5" rx="2" fill="currentColor" stroke="none" />
          </svg>
        </span>
        Select
      </span>
      <span class="hint-item">
        <span class="kbd">Shift</span> +
        <span class="kbd">
          <!-- mouse-left: composite icon, body shape based on Lucide "mouse" (ISC) with a filled left-button highlight — see NOTICE.md -->
          <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="5" y="2" width="14" height="20" rx="7" />
            <line x1="12" y1="2" x2="12" y2="8" />
            <line x1="5" y1="8" x2="19" y2="8" />
            <rect x="6" y="3" width="6" height="5" rx="2" fill="currentColor" stroke="none" />
          </svg>
        </span>
        Multi-select
      </span>
      <span class="hint-item">
        <span class="kbd">Drag</span>
        Move
      </span>
      <span class="hint-item">
        <span class="kbd">x</span>
        Delete
      </span>
      <span class="hint-item">
        <span class="kbd">c</span>
        Capture
      </span>
      <span class="hint-item">
        <span class="kbd">Esc</span>
        Clear selection
      </span>
      <span class="hint-item">
        <span class="kbd">Ctrl</span> + <span class="kbd">Z</span>
        Undo
      </span>
    {:else}
      <span class="hint-item">
        <span class="kbd">Tab</span>
        Edit
      </span>
    {/if}
  </div>
  <span class="version">v{VERSION}</span>
</div>

<style>
  .status-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    height: 22px;
    padding: 0 10px;
    flex-shrink: 0;
    background: var(--panel-bg);
    border-top: 1px solid var(--bg);
  }

  .hint {
    display: flex;
    align-items: center;
    gap: 16px;
    overflow-x: auto;
    scrollbar-width: none;
  }
  .hint::-webkit-scrollbar { display: none; }

  .hint-item {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.55);
    white-space: nowrap;
    flex-shrink: 0;
  }

  .kbd {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    padding: 1px 5px;
    border: 1px solid rgba(var(--text-rgb), 0.25);
    border-radius: 4px;
    color: var(--text);
    font-size: 9px;
    font-weight: 600;
    line-height: 1.5;
  }

  .version {
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.29);
    flex-shrink: 0;
    font-variant-numeric: tabular-nums;
  }
</style>
