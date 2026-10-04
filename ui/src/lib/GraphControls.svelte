<script>
  // Graph display controls, shown in the graph's top-right corner in Edit mode
  // (2026-10-04 kullanıcı) — replaces the "⋮" settings popup. One column of
  // three: N toggle on top, then +, then −.
  import { createEventDispatcher } from 'svelte'
  const dispatch = createEventDispatcher()

  export let size       = 0      // current effective px size — always a MODULE multiple
  export let showCounts = true
  // Fullscreen: its display size comes from the window, not from the row's own
  // paramViewerSize, so +/− would do nothing visible there — only N is shown.
  export let sizeLocked = false

  // Must match ParamViewerRow's MODULE (one slider row per step).
  const MODULE = 44
  const step = d => dispatch('sizeChange', Math.max(MODULE, size + d * MODULE))
</script>

<!-- svelte-ignore a11y-click-events-have-key-events -->
<!-- svelte-ignore a11y-no-static-element-interactions -->
<div class="graph-controls" on:click|stopPropagation>
  <button class="gc" class:on={showCounts} aria-pressed={showCounts}
      title="Show item counts (N)" on:click={() => dispatch('showCountsChange', !showCounts)}>N</button>
  {#if !sizeLocked}
    <button class="gc" title="Larger graph" aria-label="Larger graph" on:click={() => step(1)}>
      <svg width="8" height="8" viewBox="0 0 8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><path d="M4 0.5v7M0.5 4h7"/></svg>
    </button>
    <button class="gc" title="Smaller graph" aria-label="Smaller graph" disabled={size <= MODULE} on:click={() => step(-1)}>
      <svg width="8" height="8" viewBox="0 0 8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><path d="M0.5 4h7"/></svg>
    </button>
  {/if}
</div>

<style>
  .graph-controls {
    position: absolute;
    top: 8px;
    right: 8px;
    z-index: 2;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .gc {
    width: 20px;
    height: 20px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    border: none;
    border-radius: 4px;
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.5);
    font: inherit;
    font-size: 10px;
    font-weight: 700;
    cursor: pointer;
    transition: background 0.1s, color 0.15s;
  }
  .gc:hover:not(:disabled) { background: var(--border); color: rgba(var(--text-rgb), 0.85); }
  .gc.on { background: rgba(var(--accent-rgb), 0.22); color: var(--accent-light); }
  .gc.on:hover:not(:disabled) { background: rgba(var(--accent-rgb), 0.32); }
  .gc:disabled { opacity: 0.35; cursor: default; }
</style>
