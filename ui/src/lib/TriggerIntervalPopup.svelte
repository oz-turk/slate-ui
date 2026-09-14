<script>
  import { createEventDispatcher } from 'svelte'
  import { clickOutside } from './actions.js'
  const dispatch = createEventDispatcher()

  export let x = 0
  export let y = 0
  export let interval = -1000   // signed ms — negative = Manual, positive = cyclic

  // Mirrors the native right-click Interval submenu's preset list exactly
  // (see yapilacaklar/trigger-capture.md), so the label shown here always
  // matches what GH itself would display for the same value.
  const PRESETS = [
    { ms: 10, label: '10 ms' },
    { ms: 20, label: '20 ms' },
    { ms: 50, label: '50 ms' },
    { ms: 100, label: '100 ms' },
    { ms: 200, label: '200 ms' },
    { ms: 500, label: '500 ms' },
    { ms: 1000, label: '1 second' },
    { ms: 2000, label: '2 seconds' },
    { ms: 5000, label: '5 seconds' },
    { ms: 10000, label: '10 seconds' },
    { ms: 30000, label: '30 seconds' },
    { ms: 60000, label: '1 minute' },
    { ms: 300000, label: '5 minutes' },
    { ms: 900000, label: '15 minutes' },
    { ms: 3600000, label: '1 hour' },
  ]

  $: isManual = interval < 0
  $: magnitude = Math.abs(interval) || 1000

  function pickManual() {
    dispatch('change', { interval: -magnitude, intervalString: '----------' })
  }
  function pickPreset(preset) {
    dispatch('change', { interval: preset.ms, intervalString: preset.label })
  }

  // Same ms→label rule as formatMs in TriggerRow.svelte, kept local since
  // this is the only other place custom values get a display string.
  function formatMs(ms) {
    if (ms < 1000) return `${ms} ms`
    if (ms < 60000) { const s = ms / 1000; return `${s} second${s === 1 ? '' : 's'}` }
    if (ms < 3600000) { const m = ms / 60000; return `${m} minute${m === 1 ? '' : 's'}` }
    const h = ms / 3600000
    return `${h} hour${h === 1 ? '' : 's'}`
  }

  let customText = String(magnitude)
  function commitCustom() {
    const ms = Math.max(1, Math.round(Number(customText)))
    if (!Number.isFinite(ms) || ms <= 0) return
    dispatch('change', { interval: ms, intervalString: formatMs(ms) })
  }
</script>

<div class="popup" use:clickOutside={{ onClose: () => dispatch('close') }} style="left: {x}px; top: {y}px">
  <button class="item manual" class:active={isManual} on:click={pickManual}>Manual</button>

  <div class="list">
    {#each PRESETS as preset (preset.ms)}
      <button class="item" class:active={!isManual && interval === preset.ms} on:click={() => pickPreset(preset)}>
        {preset.label}
      </button>
    {/each}
  </div>

  <div class="custom">
    <input type="number" min="1" step="1" bind:value={customText}
        on:keydown={e => e.key === 'Enter' && commitCustom()}
        placeholder="Custom ms" />
    <button class="commit" on:click={commitCustom}>Set</button>
  </div>
</div>

<style>
  .popup {
    position: fixed;
    width: 168px;
    background: var(--panel-bg);
    border: 1px solid var(--grid);
    border-radius: 6px;
    padding: 8px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
    z-index: 1000;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .list {
    display: flex;
    flex-direction: column;
    gap: 1px;
    max-height: 220px;
    overflow-y: auto;
    border-top: 1px solid var(--edge-tint);
    border-bottom: 1px solid var(--edge-tint);
    padding: 4px 0;
  }

  .item {
    text-align: left;
    padding: 5px 8px;
    border: none;
    border-radius: 4px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.75);
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
  }
  .item:hover  { background: var(--grid); color: var(--text); }
  .item.active { background: rgba(var(--accent-rgb), 0.25); color: var(--text); }
  .item.manual { font-weight: 600; }

  .custom {
    display: flex;
    gap: 4px;
  }
  .custom input {
    flex: 1;
    width: 0;
    min-width: 0;
    padding: 4px 6px;
    border: 1px solid var(--grid);
    border-radius: 4px;
    background: var(--bg);
    color: var(--text);
    font-size: 11px;
    font-family: inherit;
  }
  .commit {
    padding: 4px 8px;
    border: 1px solid var(--grid);
    border-radius: 4px;
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.75);
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
    flex-shrink: 0;
  }
  .commit:hover { color: var(--text); border-color: var(--border); }
</style>
