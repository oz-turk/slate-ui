<script>
  import { createEventDispatcher } from 'svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  let dragging  = false
  let trackEl
  let lastValue = slider.value  // tracked so pointerUp can commit the final value

  // ── row drag (reorder) — see rowDrag.js ────────────────────────────────────────
  let rowEl
  let rowDragging    = false
  let dragTranslateY = 0   // small elastic follow when dragged past the first/last row

  function onHandleDrag(e) {
    const v = dragTranslateYFor(e, rowEl, isFirst, isLast)
    if (v !== null) dragTranslateY = v
  }

  $: pct = slider.max === slider.min
    ? 0
    : ((slider.value - slider.min) / (slider.max - slider.min)) * 100

  // GH's own slider rounds to this many decimals (0 for an integer slider) —
  // captured at capture/restore time from GH_SliderBase.DecimalPlaces. Falls
  // back to the old range-based guess only for the (shouldn't-happen) case
  // where a slider object predates this field.
  $: decimals = slider.decimalPlaces ?? (slider.max - slider.min < 10 ? 3 : slider.max - slider.min < 100 ? 2 : 1)

  function round(v) {
    const f = 10 ** decimals
    return Math.round(v * f) / f
  }

  function fromPct(p) {
    return round(slider.min + (slider.max - slider.min) * Math.max(0, Math.min(1, p)))
  }

  function getValueFromEvent(e) {
    const rect = trackEl.getBoundingClientRect()
    return fromPct((e.clientX - rect.left) / rect.width)
  }

  function onTrackPointerDown(e) {
    e.preventDefault()
    dragging = true
    trackEl.setPointerCapture(e.pointerId)
    lastValue = getValueFromEvent(e)
    dispatch('change', lastValue)
  }

  function onTrackPointerMove(e) {
    if (!dragging) return
    lastValue = getValueFromEvent(e)
    dispatch('change', lastValue)
  }

  function onTrackPointerUp() {
    if (dragging) dispatch('commit', lastValue)  // guaranteed final send
    dragging = false
  }

  function onInputChange(e) {
    const v = parseFloat(e.target.value)
    if (!isNaN(v)) {
      const clamped = round(Math.max(slider.min, Math.min(slider.max, v)))
      lastValue = clamped
      dispatch('change',  clamped)
      dispatch('commit',  clamped)  // input is already "committed"
    }
  }

  function fmt(v) {
    return (+v).toFixed(decimals)
  }

</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected
    class:row-dragging={rowDragging} class:row-last={isLast}
    bind:this={rowEl}
    style={dragTranslateY ? `transform: translateY(${dragTranslateY}px)` : ''}
    on:click={e => mode === 'edit' && dispatch('select', { shift: e.shiftKey, ctrl: e.ctrlKey })}
    on:dragenter|preventDefault={e => e.dataTransfer.dropEffect = 'move'}
    on:dragover|preventDefault={e => rowDragOver(e, dispatch)}
    on:dragleave={e => rowDragLeave(e, dispatch)}
    on:drop|preventDefault={e => rowDrop(e, dispatch)}
>
  {#if mode === 'edit'}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <div class="handle"
        draggable="true"
        on:click|stopPropagation
        on:dragstart={e => { e.dataTransfer.effectAllowed = 'move'; rowDragging = true; dispatch('dragStart') }}
        on:drag={onHandleDrag}
        on:dragend={() => { rowDragging = false; dragTranslateY = 0; dispatch('dragEnd') }}
    >
      <svg width="8" height="12" viewBox="0 0 8 12" fill="currentColor">
        <circle cx="2" cy="2"  r="1.2"/><circle cx="6" cy="2"  r="1.2"/>
        <circle cx="2" cy="6"  r="1.2"/><circle cx="6" cy="6"  r="1.2"/>
        <circle cx="2" cy="10" r="1.2"/><circle cx="6" cy="10" r="1.2"/>
      </svg>
    </div>
  {/if}

  <span class="name" title={slider.name}>{slider.name}</span>

  <span class="bound lo">{fmt(slider.min)}</span>

  <!-- svelte-ignore a11y-no-noninteractive-element-interactions -->
  <div
    class="track"
    role="slider"
    aria-valuemin={slider.min}
    aria-valuemax={slider.max}
    aria-valuenow={slider.value}
    tabindex="0"
    bind:this={trackEl}
    on:pointerdown={onTrackPointerDown}
    on:pointermove={onTrackPointerMove}
    on:pointerup={onTrackPointerUp}
    on:pointercancel={onTrackPointerUp}
  >
    <div class="fill" style="width: {pct}%"></div>
    <div class="thumb" style="left: {pct}%" class:dragging></div>
  </div>

  <span class="bound hi">{fmt(slider.max)}</span>

  <input
    class="val"
    type="number"
    value={fmt(slider.value)}
    min={slider.min}
    max={slider.max}
    step={10 ** -decimals}
    on:change={onInputChange}
  />

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
  {/if}
</div>

<style>
  .row {
    display: grid;
    grid-template-columns: var(--name-col-w, 110px) 44px 1fr 44px 68px;
    align-items: center;
    gap: 0;
    padding: 0 12px;
    height: 44px;
    transition: background 0.1s, transform 0.08s ease-out;
    position: relative;
  }
  /* No trailing line after the last row of a list — a divider only makes
     sense between two rows. */
  .row:not(.row-last)::after {
    content: '';
    position: absolute;
    left: 12px;
    right: 12px;
    bottom: 0;
    height: 1px;
    background: var(--edge-tint);
    pointer-events: none;
  }
  .row:hover          { background: var(--bg); }
  .row.edit           { grid-template-columns: 20px var(--name-col-w, 110px) 44px 1fr 44px 68px 24px; padding: 0 8px 0 6px; }
  .row.selected       { background: rgba(var(--accent-rgb), 0.15); }
  .row.selected:hover { background: rgba(var(--accent-rgb), 0.22); }
  .row.row-dragging   { opacity: 0.5; position: relative; z-index: 2; }

  .handle {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 20px;
    height: 100%;
    color: rgba(var(--text-rgb), 0.21);
    cursor: grab;
    flex-shrink: 0;
  }
  .handle:hover { color: rgba(var(--text-rgb), 0.43); }
  .handle:active { cursor: grabbing; }

  .name {
    font-size: 12px;
    font-weight: 500;
    color: rgba(var(--text-rgb), 0.85);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    padding-right: 8px;
  }

  .bound {
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.29);
    font-variant-numeric: tabular-nums;
  }
  .bound.lo { text-align: right; padding-right: 6px; }
  .bound.hi { text-align: left;  padding-left: 6px; }

  .track {
    position: relative;
    height: 20px;
    cursor: ew-resize;
    display: flex;
    align-items: center;
  }
  .track::before {
    content: '';
    position: absolute;
    inset: 0;
    top: 50%;
    transform: translateY(-50%);
    height: 2px;
    background: var(--grid);
    border-radius: 1px;
  }

  .fill {
    position: absolute;
    left: 0;
    top: 50%;
    transform: translateY(-50%);
    height: 2px;
    background: var(--accent);
    border-radius: 1px;
    pointer-events: none;
  }

  .thumb {
    position: absolute;
    top: 50%;
    transform: translate(-50%, -50%);
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: var(--accent);
    pointer-events: none;
    transition: transform 0.1s, background 0.1s;
  }
  .thumb.dragging {
    transform: translate(-50%, -50%) scale(1.3);
    background: var(--accent-light);
  }

  .val {
    width: 100%;
    background: transparent;
    border: none;
    border-bottom: 1px solid transparent;
    color: rgba(var(--text-rgb), 0.58);
    font-size: 11px;
    font-family: 'Segoe UI Mono', 'Consolas', monospace;
    font-variant-numeric: tabular-nums;
    text-align: right;
    padding: 2px 0 2px 4px;
    outline: none;
    transition: border-color 0.15s, color 0.15s;
    -moz-appearance: textfield;
  }
  .val::-webkit-inner-spin-button { display: none; }
  .val:focus { border-bottom-color: rgba(var(--accent-rgb), 0.4); color: rgba(var(--text-rgb), 0.85); }

  .del {
    width: 20px;
    height: 20px;
    border: none;
    background: transparent;
    color: rgba(var(--text-rgb), 0.21);
    font-size: 14px;
    cursor: pointer;
    border-radius: 3px;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: background 0.1s, color 0.1s;
    margin-left: 4px;
    padding: 0;
  }
  .del:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }
</style>
