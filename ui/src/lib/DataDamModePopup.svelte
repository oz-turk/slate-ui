<script>
  import { createEventDispatcher } from 'svelte'
  import { clickOutside } from './actions.js'
  const dispatch = createEventDispatcher()

  export let x = 0
  export let y = 0
  export let mode = 'never'          // 'always' | 'never' | 'delay'
  export let delaySeconds = 0
  export let gateMode = 'once'       // 'once' | 'always' — see "Gate Options" below

  // Mirrors the native right-click Mode/Delay submenu exactly (one combined
  // list, Always at top then presets then Never — confirmed from a live
  // screenshot, see yapilacaklar/data-dam-capture.md). No free-text entry:
  // the native menu doesn't have one either.
  const PRESETS = [0.25, 0.5, 1.0, 2.0, 10.0]

  function formatSeconds(seconds) {
    return `${seconds.toFixed(2).replace(/0$/, '').replace(/\.$/, '.0')} seconds`
  }

  function pickAlways() {
    dispatch('change', { kind: 'mode', mode: 'always', delaySeconds: 0, delayLabel: 'Always' })
  }
  function pickDelay(seconds) {
    dispatch('change', { kind: 'mode', mode: 'delay', delaySeconds: seconds, delayLabel: formatSeconds(seconds) })
  }
  function pickNever() {
    dispatch('change', { kind: 'mode', mode: 'never', delaySeconds: 0, delayLabel: 'Never' })
  }

  // Gate Options — a second, independent axis from Mode/Delay above (see
  // data-dam-capture.md): whether a linked gate fires once per false→true
  // edge, or keeps the dam acting like BufferMode.Always for as long as it
  // reads true, WITHOUT ever touching the dam's own Mode (PushDataDamGateUpdates
  // in SlateWindow.cs). The header below is what keeps these visually
  // distinct from the Mode list even though both live in one popup.
  function pickGateMode(value) {
    dispatch('change', { kind: 'gateMode', gateMode: value })
  }
</script>

<div class="popup" use:clickOutside={{ onClose: () => dispatch('close') }} style="left: {x}px; top: {y}px">
  <button class="item edge" class:active={mode === 'always'} on:click={pickAlways}>Always</button>

  <div class="list">
    {#each PRESETS as seconds (seconds)}
      <button class="item" class:active={mode === 'delay' && delaySeconds === seconds} on:click={() => pickDelay(seconds)}>
        {formatSeconds(seconds)}
      </button>
    {/each}
  </div>

  <button class="item edge" class:active={mode === 'never'} on:click={pickNever}>Never</button>

  <div class="section-label">Gate Options</div>
  <div class="list">
    <button class="item" class:active={gateMode === 'once'} on:click={() => pickGateMode('once')}>Trigger Once</button>
    <button class="item" class:active={gateMode === 'always'} on:click={() => pickGateMode('always')}>Act as Always</button>
  </div>
</div>

<style>
  .popup {
    position: fixed;
    width: 140px;
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
  .item.edge   { font-weight: 600; }

  .section-label {
    padding: 2px 8px 0;
    font-size: 9px;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    color: rgba(var(--text-rgb), 0.4);
  }
</style>
