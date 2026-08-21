<script>
  import { createEventDispatcher } from 'svelte'
  const dispatch = createEventDispatcher()

  export let count    = 0     // number of selected sliders
  export let tabs     = []    // all tabs [{ id, label }]
  export let activeTabId = ''
</script>

{#if count > 0}
  <div class="bar">
    <span class="count">{count} selected</span>

    <div class="actions">
      <span class="label">Move to:</span>
      {#each tabs.filter(t => t.id !== activeTabId) as tab (tab.id)}
        <button class="tab-btn" on:click={() => dispatch('moveTo', tab.id)}>{tab.label}</button>
      {/each}

      <div class="sep"></div>

      <button class="group-btn" on:click={() => dispatch('groupSelected')}>Group</button>
      <button class="clear-btn" on:click={() => dispatch('clearSelection')}>✕</button>
    </div>
  </div>
{/if}

<style>
  .bar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 0 12px;
    height: 34px;
    background: rgba(var(--accent-rgb), 0.08);
    border-bottom: 1px solid rgba(var(--accent-rgb), 0.27);
    flex-shrink: 0;
  }

  .count {
    font-size: 11px;
    color: var(--accent-light);
    font-weight: 600;
    white-space: nowrap;
  }

  .actions {
    display: flex;
    align-items: center;
    gap: 4px;
    flex: 1;
    overflow: hidden;
  }

  .label {
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.29);
    white-space: nowrap;
  }

  .tab-btn {
    padding: 2px 8px;
    height: 22px;
    border: 1px solid rgba(var(--accent-rgb), 0.27);
    background: rgba(var(--accent-rgb), 0.15);
    color: var(--accent-light);
    font-size: 11px;
    border-radius: 3px;
    cursor: pointer;
    white-space: nowrap;
    transition: background 0.1s, border-color 0.1s;
  }
  .tab-btn:hover { background: rgba(var(--accent-rgb), 0.22); border-color: var(--accent); }

  .sep {
    flex: 1;
  }

  .group-btn {
    padding: 2px 10px;
    height: 22px;
    border: 1px solid var(--panel-bg);
    background: rgba(var(--panel-bg-rgb), 0.7);
    color: rgba(var(--text-rgb), 0.65);
    font-size: 11px;
    border-radius: 3px;
    cursor: pointer;
    white-space: nowrap;
    transition: background 0.1s, color 0.1s;
  }
  .group-btn:hover { background: var(--panel-bg); color: rgba(var(--text-rgb), 0.85); }

  .clear-btn {
    width: 22px;
    height: 22px;
    border: none;
    background: transparent;
    color: rgba(var(--text-rgb), 0.29);
    font-size: 12px;
    cursor: pointer;
    border-radius: 3px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    transition: background 0.1s, color 0.1s;
  }
  .clear-btn:hover { background: var(--panel-bg); color: rgba(var(--text-rgb), 0.58); }
</style>
