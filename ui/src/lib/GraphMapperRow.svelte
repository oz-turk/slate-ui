<script>
  import ParamIcon from './ParamIcon.svelte'
  import { createEventDispatcher } from 'svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  // { id, name, types: [{ id, name }],
  //   value: { typeId, grips: [{ x, y, c }], path: [[x, y]...], locked } }
  // All coordinates are GH's unit box (0..1, y up). `path` is the curve exactly as
  // the GH canvas draws it (the graph's own GDI_GraphPath) — NOT ValueAt(x) samples,
  // which skip stretches once a curve-through-points graph (Interpolated, Polyline)
  // stops being a function of x. JS never reimplements a graph's maths, so the
  // curve only refreshes after a solve (the grips themselves follow the pointer
  // immediately, see dragPos).
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

  // ── graph state ───────────────────────────────────────────────────────────────
  import PadGrid from './PadGrid.svelte'
  import GraphControls from './GraphControls.svelte'
  import { hoverHint } from '../stores/uiState.js'

  const PAD_HINT = 'Drag a point to move it  ·  Ctrl + drag: snap to the ruler divisions  ·  Right-click: graph type'

  // Row height in MODULEs, same stepper and same default as MdSliderRow (see the
  // note there for how the 6 was worked out): + / − in Edit mode, persisted on
  // the slider as `padRows`.
  const MODULE = 44
  const DEFAULT_ROWS = 6
  const MIN_ROWS = 3
  $: rowPx = Math.max(MIN_ROWS, slider.padRows ?? DEFAULT_ROWS) * MODULE

  let padEl
  let hovering  = false
  let dragIndex = -1
  let dragPos   = null   // { x, y } of the grip being dragged; wins over slider.value for that grip only

  $: value  = slider.value ?? { typeId: '', grips: [], path: [] }
  // Only the grip under the pointer is local. Every other grip is shown where GH
  // says it is — many graph types move the rest themselves when one moves.
  $: grips  = (value.grips ?? []).map((g, i) => i === dragIndex && dragPos ? { ...g, ...dragPos } : g)
  $: types  = slider.types ?? []
  $: typeName = types.find(t => t.id === value.typeId)?.name ?? 'No graph'
  $: curvePoints = (value.path ?? []).map(p => `${p[0]},${1 - p[1]}`).join(' ')

  const clamp01 = v => Math.min(1, Math.max(0, v))

  // X / Y readout under the name, same style as MdSliderRow: the grip being
  // dragged, else the one under the pointer. Always rendered (blank when idle) so
  // the name does not jump when a readout appears — MdSliderRow always has them.
  let hoverIndex = -1
  $: readIndex = dragIndex >= 0 ? dragIndex : hoverIndex
  $: readGrip  = readIndex >= 0 ? grips[readIndex] : null
  const fmt = v => (Math.abs(v) >= 100 ? v.toFixed(1) : v.toFixed(3)).replace(/\.?0+$/, '')

  function unitAt(e) {
    const r = padEl.getBoundingClientRect()
    let x = clamp01((e.clientX - r.left) / r.width)
    let y = clamp01(1 - (e.clientY - r.top) / r.height)
    // Ctrl: snap to the ruler's 10 divisions (checked per event, so it can be
    // pressed or released mid-drag)
    if (e.ctrlKey) { x = Math.round(x * 10) / 10; y = Math.round(y * 10) / 10 }
    return { x, y }
  }

  // GH_GripConstraint (verified in GH_GraphContainer.RespondToMouseMove):
  // 'horizontal' keeps y fixed, 'vertical' keeps x fixed.
  function moved(i, p) {
    const g = value.grips[i]
    return {
      x: g.c === 'vertical'   ? g.x : p.x,
      y: g.c === 'horizontal' ? g.y : p.y,
    }
  }

  // `only` tells C# which grip moved (stripped from the stored value in Pane.svelte).
  function emit(name) {
    const next = value.grips.map((g, i) => i === dragIndex ? { ...g, ...dragPos } : g)
    dispatch(name, { ...value, grips: next, only: dragIndex })
  }

  function onGripDown(e, i) {
    if (value.locked) return
    dragIndex = i
    dragPos   = { x: value.grips[i].x, y: value.grips[i].y }
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  function onGripMove(e) {
    if (dragIndex < 0) return
    dragPos = moved(dragIndex, unitAt(e))
    emit('change')
  }
  function onGripUp(e) {
    if (dragIndex < 0) return
    e.currentTarget.releasePointerCapture(e.pointerId)
    emit('commit')
    dragIndex = -1
    dragPos   = null
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
    <span class="readout">{readGrip ? `X ${fmt(readGrip.x)}` : ' '}</span>
    <span class="readout">{readGrip ? `Y ${fmt(readGrip.y)}` : ' '}</span>
  </div>

  <!-- Layout shared with MdSliderRow: title line above a square-ish pad. The
       graph type is picked from the row's right-click menu (Pane.svelte). -->
  <div class="col">
    <span class="title" title="Right-click to change the graph type">{typeName}</span>
    <!-- padbox leaves room (left/bottom) for the hover ruler numbers outside the pad -->
    <div class="padbox">
      <!-- svelte-ignore a11y-mouse-events-have-key-events -->
      <div class="pad" bind:this={padEl} class:locked={value.locked} class:active={dragIndex >= 0}
          on:click|stopPropagation
          on:pointerenter={() => hovering = true}
          on:pointerleave={() => hovering = false}
          on:mouseover|stopPropagation={() => hoverHint.set(PAD_HINT)}
          on:mouseleave={() => hoverHint.set(null)}
      >
        <svg viewBox="0 0 1 1" preserveAspectRatio="none">
          <polyline points={curvePoints} class="curve"/>
        </svg>
        {#each grips as g, i}
          <div class="grip" class:active={dragIndex === i}
              style="left: {g.x * 100}%; top: {(1 - g.y) * 100}%"
              on:pointerenter={() => hoverIndex = i}
              on:pointerleave={() => hoverIndex = -1}
              on:pointerdown|stopPropagation={e => onGripDown(e, i)}
              on:pointermove={onGripMove}
              on:pointerup={onGripUp}
              on:pointercancel={onGripUp}
          ></div>
        {/each}
      </div>
      <PadGrid show={hovering || dragIndex >= 0} />
    </div>
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
    height: var(--row-h, 264px);   /* n × MODULE (44px), see rowPx — same as MdSliderRow */
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
  /* Edit mode: the pad's right edge lines up with the other rows' controls
     (dropdown/toggle) instead of running up against the +/− buttons. */
  .row.edit .col       { margin-right: 4px; }
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
       inside .padbox. Identical in MdSliderRow. */
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
  .col {
    flex: 1;
    min-width: 64px;
    height: calc(var(--row-h, 264px) - 24px);   /* 12px breathing room above and below */
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .title {
    margin-left: 32px;   /* aligned with the pad, not the ruler column */
    height: 14px;
    flex-shrink: 0;
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 10px;
    line-height: 14px;
    color: rgba(var(--text-rgb), 0.43);
    white-space: pre;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .padbox {
    flex: 1;
    min-height: 0;
    position: relative;
    box-sizing: border-box;
    padding: 0 0 18px 32px;   /* ruler ticks + numbers live in this padding — see PadGrid */
  }
  .pad {
    width: 100%;
    height: 100%;
    position: relative;
    z-index: 1;   /* grips above the hover grid/ruler overlay (PadGrid is a later sibling) */
    /* NOT overflow:hidden — a grip sitting on the edge (x or y = 0 / 1) must show
       whole, half of it outside the pad. The curve clips itself (.pad svg). */
    border: 1px solid var(--grid);
    border-radius: 4px;
    background: rgba(var(--text-rgb), 0.04);
    touch-action: none;
  }
  .pad:hover, .pad.active { border-color: var(--border); }
  .pad svg {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    overflow: hidden;
    pointer-events: none;
  }
  .curve {
    fill: none;
    stroke: rgb(var(--accent-rgb));
    stroke-width: 1.5;
    vector-effect: non-scaling-stroke;
    stroke-linejoin: round;
  }

  .grip {
    position: absolute;
    width: 10px;
    height: 10px;
    margin: -5px 0 0 -5px;
    border-radius: 50%;
    background: var(--bg, #1e1e1e);
    border: 2px solid rgb(var(--accent-rgb));
    cursor: grab;
    touch-action: none;
    /* Points only show while the pointer is over the pad (or a drag is running) —
       at rest only the curve is visible. They stay hit-testable while hidden:
       entering the pad is what reveals them, so there is nothing to miss. */
    opacity: 0;
    transition: opacity 0.12s;
  }
  .pad:hover .grip, .pad.active .grip { opacity: 1; }
  .grip:hover  { background: rgb(var(--accent-rgb)); }
  .grip.active { background: rgb(var(--accent-rgb)); cursor: grabbing; box-shadow: 0 0 0 3px rgba(var(--accent-rgb), 0.3); }
  .pad.locked:hover .grip { cursor: default; opacity: 0.5; }

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
