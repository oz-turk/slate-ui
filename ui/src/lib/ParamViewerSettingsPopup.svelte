<script>
  // Settings popup for a Param Viewer row's inline preview (ParamViewerRow.svelte)
  // — same trigger pattern as PanelRow's TextSettingsPopup (a small "⋮" icon
  // opening a fixed-position popup), but for the sunburst's own size and
  // count display rather than text appearance.
  import { createEventDispatcher } from 'svelte'
  import { clickOutside } from './actions.js'
  const dispatch = createEventDispatcher()

  // Moves the popup to a direct child of <body> instead of leaving it nested
  // inside the pane's row list (2026-09-26 kullanıcı: "+'ya bastığımda window
  // kapanıp yeniden açılıyor gibi görünüyor... yeri konumu değişmemeli").
  // Root cause: clicking +/- grows this row's height, which shifts every
  // row below it, and Pane.svelte/GroupSection.svelte animate that shift
  // with `animate:flip` on each row-slot — which applies an inline
  // `transform` to the row-slot for the transition's duration (even ours,
  // whose OWN position doesn't change, since Svelte still sets an identity
  // transform while it runs). Any `transform` on an ancestor makes it the
  // containing block for a `position: fixed` descendant instead of the
  // viewport, so for that ~150ms this popup would briefly reposition
  // relative to its row, then snap back — read as "closing and reopening".
  // Portaling out from under the animated row-slot sidesteps it entirely,
  // regardless of timing.
  function portal(node) {
    document.body.appendChild(node)
    return { destroy() { node.remove() } }
  }

  export let x = 0
  export let y = 0
  export let size       = 0     // current effective px size — always a MODULE multiple
  export let isCustom   = false // true once the row has its own override (slider.paramViewerSize), shows the reset
  export let showCounts = true

  // Must match ParamViewerRow's own MODULE — kept as a separate literal
  // rather than a shared import since it's one small UI constant, same
  // reasoning TextSettingsPopup gives for not sharing its slider mechanics.
  const MODULE = 44

  function step(delta) {
    dispatch('sizeChange', Math.max(MODULE, size + delta * MODULE))
  }
  function resetSize()    { dispatch('sizeChange', null) }
  function toggleCounts() { dispatch('showCountsChange', !showCounts) }
</script>

<!-- svelte-ignore a11y-click-events-have-key-events -->
<!-- svelte-ignore a11y-no-static-element-interactions -->
<div class="popup" use:portal on:click|stopPropagation use:clickOutside={{ onClose: () => dispatch('close') }} style="left: {x}px; top: {y}px">
  <div class="section-label">Size</div>
  <div class="stepper">
    <button on:click={() => step(-1)} title="Shrink by one row">−</button>
    <span class="value">{size}px<span class="rows">({size / MODULE}×)</span></span>
    <button on:click={() => step(1)} title="Grow by one row">+</button>
    <!-- Always shown, not just once customised (2026-09-26 kullanıcı: "reset
         to default tuşu da hep görünmeli sadece default'taysa gri olup
         disabled olmalı") — previously only rendered once isCustom, which
         made the layout shift width when it first appeared. -->
    <button class="reset-btn" on:click={resetSize} disabled={!isCustom} title="Reset to default">×</button>
  </div>

  <div class="section-label">Display</div>
  <!-- svelte-ignore a11y-no-noninteractive-element-interactions -->
  <button class="toggle-row" on:click={toggleCounts}>
    <span>Show item counts (N)</span>
    <span class="switch" class:on={showCounts} role="switch" aria-checked={showCounts}>
      <span class="knob"></span>
    </span>
  </button>
</div>

<style>
  .popup {
    position: fixed;
    width: 176px;
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

  .section-label {
    padding: 2px 4px 0;
    font-size: 9px;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    color: rgba(var(--text-rgb), 0.4);
  }

  .stepper {
    display: grid;
    grid-template-columns: 24px 1fr 24px auto;
    align-items: center;
    gap: 4px;
    padding: 0 4px;
  }
  .stepper button {
    width: 24px;
    height: 24px;
    padding: 0;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.6);
    font-size: 13px;
    font-family: inherit;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: border-color 0.15s, color 0.15s;
  }
  .stepper button:hover { color: rgba(var(--text-rgb), 0.9); border-color: var(--border); }
  .value {
    text-align: center;
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.7);
    font-variant-numeric: tabular-nums;
  }
  .rows {
    margin-left: 3px;
    color: rgba(var(--text-rgb), 0.4);
  }
  .reset-btn {
    width: 16px;
    height: 16px;
    padding: 0;
    border: none;
    background: transparent;
    color: rgba(var(--text-rgb), 0.4);
    font-size: 12px;
    line-height: 1;
    cursor: pointer;
    border-radius: 3px;
  }
  .reset-btn:hover:not(:disabled) { background: var(--grid); color: rgba(var(--text-rgb), 0.75); }
  .reset-btn:disabled { color: rgba(var(--text-rgb), 0.15); cursor: default; }

  .toggle-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    padding: 2px 4px;
    border: none;
    background: transparent;
    cursor: pointer;
    font-family: inherit;
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.85);
    text-align: left;
  }

  .switch {
    width: 32px;
    height: 18px;
    border-radius: 9px;
    border: 1px solid var(--grid);
    background: var(--grid);
    padding: 0;
    cursor: pointer;
    position: relative;
    flex-shrink: 0;
    transition: background 0.15s, border-color 0.15s;
  }
  .switch.on { background: rgba(var(--accent-rgb), 0.4); border-color: var(--accent); }

  .knob {
    position: absolute;
    top: 1px; left: 1px;
    width: 14px; height: 14px;
    border-radius: 50%;
    background: rgba(var(--text-rgb), 0.7);
    transition: transform 0.15s, background 0.15s;
  }
  .switch.on .knob { transform: translateX(14px); background: var(--text); }
</style>
