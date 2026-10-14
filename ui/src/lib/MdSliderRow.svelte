<script>
  import ParamIcon from './ParamIcon.svelte'
  import { createEventDispatcher } from 'svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  // { id, name, value: { x, y }, xMin, xMax, yMin, yMax }
  export let slider    = {}
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  // ── row drag (reorder) — see rowDrag.js ────────────────────────────────────────
  let rowEl
  let rowDragging    = false
  let dragTranslateY = 0

  function onHandleDrag(e) {
    const v = dragTranslateYFor(e, rowEl, isFirst, isLast)
    if (v !== null) dragTranslateY = v
  }

  // ── XY pad ────────────────────────────────────────────────────────────────────
  import PadGrid from './PadGrid.svelte'
  import GraphControls from './GraphControls.svelte'
  import { hoverHint } from '../stores/uiState.js'

  const PAD_HINT = 'Drag: set X / Y  ·  Ctrl + drag: snap to the ruler divisions'

  // Row height in MODULEs (44px = one slider row), same stepper as the Param
  // Viewer / Path Mapper graph: + / − in Edit mode, persisted on the slider as
  // `padRows`. Default 6 modules, worked out by hand like ParamViewerRow's
  // AUTO_SIZE: Slate opens 380px wide (SlateWindow.DefaultWindowSize); minus the
  // row padding (24), the name column (~90-110) and the min-value column (44)
  // the pad is ~200-220px wide, plus 42px of ruler/padding around it = ~250-260
  // → rounded to the nearest module = 6 × 44 = 264. Same default in
  // GraphMapperRow (its title line makes the pad a bit shorter, row is the same).
  const MODULE = 44
  const DEFAULT_ROWS = 6
  const MIN_ROWS = 3
  $: rowPx = Math.max(MIN_ROWS, slider.padRows ?? DEFAULT_ROWS) * MODULE

  let padEl
  let dragging = false
  let hovering = false

  $: xMin = slider.xMin ?? 0
  $: xMax = slider.xMax ?? 1
  $: yMin = slider.yMin ?? 0
  $: yMax = slider.yMax ?? 1
  $: x = slider.value?.x ?? xMin
  $: y = slider.value?.y ?? yMin

  const frac = (v, lo, hi) => hi === lo ? 0 : Math.min(1, Math.max(0, (v - lo) / (hi - lo)))
  $: left = frac(x, xMin, xMax) * 100
  $: top  = (1 - frac(y, yMin, yMax)) * 100   // Y grows upward, like on the GH canvas

  const fmt = v => (Math.abs(v) >= 100 ? v.toFixed(1) : v.toFixed(3)).replace(/\.?0+$/, '')

  function valueAt(e) {
    const r  = padEl.getBoundingClientRect()
    let fx = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width))
    let fy = Math.min(1, Math.max(0, (e.clientY - r.top)  / r.height))
    // Ctrl: snap to the ruler's 10 divisions (checked per event, so it can be
    // pressed or released mid-drag)
    if (e.ctrlKey) { fx = Math.round(fx * 10) / 10; fy = Math.round(fy * 10) / 10 }
    return { x: xMin + fx * (xMax - xMin), y: yMax - fy * (yMax - yMin) }
  }

  function onDown(e) {
    dragging = true
    padEl.setPointerCapture(e.pointerId)
    dispatch('change', valueAt(e))
  }
  function onMove(e) {
    if (dragging) dispatch('change', valueAt(e))
  }
  function onUp(e) {
    if (!dragging) return
    dragging = false
    padEl.releasePointerCapture(e.pointerId)
    dispatch('commit', valueAt(e))
  }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected
    class:row-dragging={rowDragging} class:row-last={isLast}
    bind:this={rowEl}
    style="--row-h: {rowPx}px;{dragTranslateY ? ` transform: translateY(${dragTranslateY}px)` : ''}"
    on:click={e => mode === 'edit' && dispatch('select', { shift: e.shiftKey, ctrl: e.ctrlKey })}
    on:dragenter|preventDefault={e => e.dataTransfer.dropEffect = 'move'}
    on:dragover|preventDefault={e => rowDragOver(e, dispatch)}
    on:dragleave={e => rowDragLeave(e, dispatch)}
    on:drop|preventDefault={e => rowDrop(e, dispatch)}
    on:contextmenu|preventDefault|stopPropagation={e => dispatch('contextMenu', { x: e.clientX, y: e.clientY })}
>
  {#if mode === 'edit'}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <div class="handle"
        draggable="true"
        on:click|stopPropagation
        on:dragstart={e => { e.dataTransfer.effectAllowed = 'move'; rowDragging = true; dispatch('dragStart', e.altKey) }}
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

  <div class="label">
    <span class="name" title={slider.name}><ParamIcon {slider} {mode} />{slider.name}</span>
    <span class="readout">X {fmt(x)}</span>
    <span class="readout">Y {fmt(y)}</span>
  </div>

  <!-- Same label width as GraphMapperRow, so both pads are equally wide (heights differ). -->
  <!-- padbox leaves room (left/bottom) for the hover ruler numbers outside the pad -->
  <div class="padbox">
    <!-- svelte-ignore a11y-mouse-events-have-key-events -->
    <div class="pad" class:active={dragging}
        bind:this={padEl}
        on:click|stopPropagation
        on:pointerdown|stopPropagation={onDown}
        on:pointermove={onMove}
        on:pointerup={onUp}
        on:pointercancel={onUp}
        on:pointerenter={() => hovering = true}
        on:pointerleave={() => hovering = false}
        on:mouseover|stopPropagation={() => hoverHint.set(PAD_HINT)}
        on:mouseleave={() => hoverHint.set(null)}
    >
      <div class="cross-h" style="top: {top}%"></div>
      <div class="cross-v" style="left: {left}%"></div>
      <div class="thumb" style="left: {left}%; top: {top}%"></div>
    </div>
    <PadGrid show={hovering || dragging} {xMin} {xMax} {yMin} {yMax} />
  </div>

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
    <GraphControls size={rowPx} minSize={MIN_ROWS * MODULE} countsToggle={false}
        on:sizeChange={e => dispatch('patch', { padRows: Math.round(e.detail / MODULE) })} />
  {/if}
</div>

<style>
  .row {
    display: flex;
    align-items: center;
    gap: 0;
    padding: 0 12px;
    height: var(--row-h, 264px);   /* n × MODULE (44px), see rowPx */
    transition: background 0.1s, transform 0.08s ease-out;
    position: relative;
  }
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
  .row.edit            { padding: 0 8px 0 6px; }
  .row:hover          { background: var(--pane-pattern-mask, transparent); }
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
    filter:
      drop-shadow(2px 0 1.5px var(--pane-pattern-mask, transparent))
      drop-shadow(-2px 0 1.5px var(--pane-pattern-mask, transparent))
      drop-shadow(0 2px 1.5px var(--pane-pattern-mask, transparent))
      drop-shadow(0 -2px 1.5px var(--pane-pattern-mask, transparent));
  }
  .handle:hover  { color: rgba(var(--text-rgb), 0.43); }
  .handle:active { cursor: grabbing; }

  .label {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
    /* Same name column as the slider rows (--name-col-w, set by Pane), so the pad
       starts exactly where a slider's track starts: name column + the 44px
       min-value column, minus the 32px of ruler padding that sits left of the pad
       inside .padbox. Identical in GraphMapperRow. */
    width: calc(var(--name-col-w, 110px) + 12px);
    flex-shrink: 0;
  }
  .name {
    font-size: 12px;
    font-weight: 500;
    color: rgba(var(--text-rgb), 0.85);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .readout {
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.43);
    white-space: nowrap;
  }

  .padbox {
    flex: 1;
    min-width: 64px;
    height: calc(var(--row-h, 264px) - 24px);   /* 12px breathing room above and below */
    position: relative;
    box-sizing: border-box;
    padding: 0 0 18px 32px;   /* ruler ticks + numbers live in this padding — see PadGrid */
  }
  .pad {
    width: 100%;
    height: 100%;
    position: relative;
    z-index: 1;   /* thumb above the hover grid/ruler overlay (PadGrid is a later sibling) */
    /* NOT overflow:hidden — a thumb on the edge (x or y at min / max) must show
       whole, half of it outside the pad. */
    border: 1px solid var(--grid);
    border-radius: 4px;
    background: rgba(var(--text-rgb), 0.04);
    cursor: crosshair;
    touch-action: none;
  }
  .pad:hover, .pad.active { border-color: var(--border); }

  .cross-h, .cross-v {
    position: absolute;
    background: rgba(var(--accent-rgb), 0.35);
    pointer-events: none;
  }
  .cross-h { left: 0; right: 0; height: 1px; }
  .cross-v { top: 0; bottom: 0; width: 1px; }

  .thumb {
    position: absolute;
    width: 10px;
    height: 10px;
    margin: -5px 0 0 -5px;
    border-radius: 50%;
    background: rgb(var(--accent-rgb));
    pointer-events: none;
  }
  .pad.active .thumb { box-shadow: 0 0 0 3px rgba(var(--accent-rgb), 0.3); }

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
    flex-shrink: 0;
    text-shadow:
      2px 0 1.5px var(--pane-pattern-mask, transparent), -2px 0 1.5px var(--pane-pattern-mask, transparent),
      0 2px 1.5px var(--pane-pattern-mask, transparent), 0 -2px 1.5px var(--pane-pattern-mask, transparent),
      2px 2px 1.5px var(--pane-pattern-mask, transparent), -2px -2px 1.5px var(--pane-pattern-mask, transparent),
      2px -2px 1.5px var(--pane-pattern-mask, transparent), -2px 2px 1.5px var(--pane-pattern-mask, transparent);
    transition: background 0.1s, color 0.1s;
    padding: 0;
  }
  .del:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }
</style>
