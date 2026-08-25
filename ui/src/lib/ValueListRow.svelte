<script>
  import { afterUpdate } from 'svelte'
  import { createEventDispatcher } from 'svelte'
  import { hoverHint } from '../stores/uiState.js'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  import { resizeHint, measureResizeBounds, computeResizeHeight } from './resizeHandle.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, value, options, multiSelect }
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  $: options     = slider.options ?? []
  $: selectedSet = new Set(slider.multiSelect && Array.isArray(slider.value) ? slider.value : [])

  const MODULE = 44   // one slider row's height — the app's base sizing unit

  // ── row drag (reorder) — see rowDrag.js ────────────────────────────────────────
  let rowEl
  let rowDragging    = false
  let dragTranslateY = 0

  // ── checklist resize — see resizeHandle.js. default is unset (fits every
  // option, no scroll); once dragged, slider.height takes over as an explicit,
  // scrollable height. Same custom handle as PanelRow: capped to the pane's
  // visible area, Ctrl snaps to MODULE increments.
  let resizing     = false
  let resizeStartY = 0
  let resizeStartH = 0
  let liveHeight   = 0
  let resizeOverhead = 0
  let maxBodyHeight  = Infinity   // capped to the pane's visible area — can't drag past the window
  let lastCtrlKey = false

  // ── un-resized default height: fit every option with no scroll, snapped up
  // to whole MODULEs so an N-item checklist still lands on the row grid —
  // e.g. a 5-item list otherwise ends up whatever height flex gives it.
  // Options render single-line (ellipsis, no wrap), so one item's own height
  // is a fixed constant independent of the container's own sizing — safe to
  // measure it directly with no risk of it feeding back into itself.
  //
  // Snapping the checklist's own height alone isn't enough — same issue the
  // manual resize handle already solves via resizeOverhead (see
  // resizeHandle.js): the header/margin/padding/border around it is ~59px,
  // not itself a multiple of MODULE, so a checklist that's an exact multiple
  // of MODULE still leaves the ROW's outer edge off-grid. Overhead is
  // measured live off rowEl (like measureResizeBounds does), not hardcoded,
  // so it keeps working if the surrounding layout ever changes.
  let checklistEl
  let itemHeight = 0
  let overhead   = 0
  afterUpdate(() => {
    const h = checklistEl?.querySelector('.check-item')?.offsetHeight ?? 0
    if (h && h !== itemHeight) itemHeight = h
    if (rowEl && checklistEl) {
      const ov = rowEl.offsetHeight - checklistEl.offsetHeight
      if (ov !== overhead) overhead = ov
    }
  })
  const CHECKLIST_PAD = 8   // .checklist padding: 4px top + 4px bottom
  const ITEM_GAP      = 1   // .checklist gap: 1px between items
  $: naturalChecklistHeight = itemHeight
    ? CHECKLIST_PAD + options.length * itemHeight + Math.max(0, options.length - 1) * ITEM_GAP
    : 0
  // Math.max clamps the TOTAL (overhead + checklist) to at least one MODULE,
  // not the checklist alone — see the matching comment in PanelRow.svelte.
  $: fitChecklistHeight = naturalChecklistHeight
    ? Math.max(MODULE, Math.ceil((overhead + naturalChecklistHeight) / MODULE) * MODULE) - overhead
    : MODULE

  function onResizeDown(e) {
    e.preventDefault()
    e.stopPropagation()
    resizing      = true
    resizeStartY  = e.clientY
    resizeStartH  = slider.height ?? e.currentTarget.previousElementSibling?.offsetHeight ?? MODULE * 3
    liveHeight    = resizeStartH
    lastCtrlKey   = false
    ;({ resizeOverhead, maxBodyHeight } = measureResizeBounds(rowEl, resizeStartH))
    hoverHint.set(resizeHint(liveHeight, false))
    dispatch('resizeStart', slider.id)
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  function computeHeight(e, snapped = e.ctrlKey) {
    const h = computeResizeHeight({ clientY: e.clientY, resizeStartY, resizeStartH, resizeOverhead, maxBodyHeight, snapped, MODULE })
    return { h, snapped }
  }
  function onResizeMove(e) {
    if (!resizing) return
    lastCtrlKey = e.ctrlKey
    const { h, snapped } = computeHeight(e)
    liveHeight = h
    hoverHint.set(resizeHint(h, snapped))
    dispatch('resize', h)
  }
  function onResizeUp(e) {
    if (!resizing) return
    resizing = false
    // Recompute from the pointerup event's own clientY — pointermove can
    // coalesce/drop under the browser, so the last onResizeMove reading can
    // lag behind the actual release point. OR the ctrlKey with the last move's
    // reading too: releasing Ctrl a hair before the mouse button is a common
    // muscle-memory pattern, and shouldn't silently drop the snap on release.
    liveHeight = computeHeight(e, e.ctrlKey || lastCtrlKey).h
    hoverHint.set(null)
    dispatch('resizeCommit', liveHeight)
    dispatch('resizeEnd')
  }

  function onHandleDrag(e) {
    const v = dragTranslateYFor(e, rowEl, isFirst, isLast)
    if (v !== null) dragTranslateY = v
  }

  function onSelect(e) {
    dispatch('change', +e.target.value)
  }

  function onCycle(delta) {
    if (!options.length) return
    const current = typeof slider.value === 'number' ? slider.value : -1
    const next = current + delta
    // Cycle wraps at the ends; Sequence (loop=false) stops there instead.
    const wrapped = slider.loop ? (next + options.length) % options.length : Math.max(0, Math.min(options.length - 1, next))
    if (wrapped === current) return
    dispatch('change', wrapped)
  }

  function onCycleWheel(e) {
    e.preventDefault()
    onCycle(e.deltaY < 0 ? 1 : -1)
  }

  function onToggle(i) {
    dispatch('change', i)
  }

</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected class:multi={slider.multiSelect}
    class:row-dragging={rowDragging}
    bind:this={rowEl}
    style={dragTranslateY ? `transform: translateY(${dragTranslateY}px)` : ''}
    on:click={e => mode === 'edit' && dispatch('select', { shift: e.shiftKey, ctrl: e.ctrlKey })}
    on:dragenter|preventDefault={e => e.dataTransfer.dropEffect = 'move'}
    on:dragover|preventDefault={e => rowDragOver(e, dispatch)}
    on:dragleave={e => rowDragLeave(e, dispatch)}
    on:drop|preventDefault={e => rowDrop(e, dispatch)}
>
  <div class="header">
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

    <div class="spacer"></div>

    {#if !slider.multiSelect && slider.cycle}
      <div class="picker cycle" on:wheel|stopPropagation={onCycleWheel} title="Scroll, or use the arrows, to change the value">
        <button class="cycle-arrow" disabled={!slider.loop && slider.value <= 0} on:click|stopPropagation={() => onCycle(-1)} tabindex="-1" title="Previous value">
          <svg width="6" height="10" viewBox="0 0 6 10" fill="currentColor"><path d="M6 0L0 5L6 10Z"/></svg>
        </button>
        <span class="cycle-value">{options[slider.value] ?? ''}</span>
        <button class="cycle-arrow" disabled={!slider.loop && slider.value >= options.length - 1} on:click|stopPropagation={() => onCycle(1)} tabindex="-1" title="Next value">
          <svg width="6" height="10" viewBox="0 0 6 10" fill="currentColor"><path d="M0 0L6 5L0 10Z"/></svg>
        </button>
      </div>
    {:else if !slider.multiSelect}
      <select class="picker" value={slider.value} on:click|stopPropagation on:change={onSelect}>
        {#each options as opt, i}
          <option value={i}>{opt}</option>
        {/each}
      </select>
    {/if}

    {#if mode === 'edit'}
      <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
    {/if}
  </div>

  {#if slider.multiSelect}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <div class="checklist" class:capped={slider.height != null} bind:this={checklistEl}
        style={slider.height != null ? `height: ${slider.height}px` : `min-height: ${fitChecklistHeight}px`}
        on:click|stopPropagation>
      {#each options as opt, i (i)}
        <label class="check-item">
          <input type="checkbox" checked={selectedSet.has(i)} on:change={() => onToggle(i)} />
          <span>{opt}</span>
        </label>
      {/each}
    </div>

    {#if mode === 'edit'}
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <div class="resize-handle" class:resizing
          on:pointerdown={onResizeDown}
          on:pointermove={onResizeMove}
          on:pointerup={onResizeUp}
          on:pointercancel={onResizeUp}
          on:click|stopPropagation
          title="Drag to resize — hold Ctrl to snap to slider-row increments"
      >
        <span class="grip"></span>
      </div>
    {/if}
  {/if}
</div>

<style>
  .row {
    display: flex;
    align-items: center;
    gap: 0;
    padding: 0 12px;
    height: 44px;
    transition: background 0.1s, transform 0.08s ease-out;
    position: relative;
  }
  .row::after {
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
  .row:hover          { background: var(--bg); }
  .row.selected       { background: rgba(var(--accent-rgb), 0.15); }
  .row.selected:hover { background: rgba(var(--accent-rgb), 0.22); }
  .row.row-dragging   { opacity: 0.5; position: relative; z-index: 2; }

  /* multiSelect (CheckList/Sequence) grows into a header + checklist body,
     same shape as PanelRow, instead of the single compact line */
  .row.multi {
    flex-direction: column;
    align-items: stretch;
    height: auto;
    padding: 0 12px 10px;
  }
  .row.multi.edit { padding: 0 8px 10px 6px; }

  .header {
    display: flex;
    align-items: center;
    width: 100%;
    height: 44px;
  }
  .row.multi .header { margin-bottom: 4px; }

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
  .handle:hover  { color: rgba(var(--text-rgb), 0.43); }
  .handle:active { cursor: grabbing; }

  .name {
    font-size: 12px;
    font-weight: 500;
    color: rgba(var(--text-rgb), 0.85);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .spacer { flex: 1; }

  .picker {
    width: 140px;
    height: 24px;
    padding: 0 22px 0 8px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.85);
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
    flex-shrink: 0;
    appearance: none;
    -webkit-appearance: none;
    background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='8' height='6' viewBox='0 0 8 6'%3E%3Cpath d='M0 0L4 6L8 0Z' fill='%23efefef' fill-opacity='0.5'/%3E%3C/svg%3E");
    background-repeat: no-repeat;
    background-position: right 8px center;
    transition: border-color 0.15s;
  }
  .picker:hover, .picker:focus { border-color: var(--border); outline: none; }
  .picker option {
    background: var(--panel-bg);
    color: var(--text);
  }

  /* Cycle mode shares the DropDown picker's footprint but is a prev/value/next
     trio (click an arrow, or scroll anywhere on it) rather than a menu —
     mirrors GH's own "◀ value ▶" look for Sequence/Cycle value lists. */
  .picker.cycle {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 4px;
    background-image: none;
    padding: 0 4px;
  }
  .cycle-value {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    text-align: center;
  }
  .cycle-arrow {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    width: 18px;
    height: 100%;
    padding: 0;
    background: none;
    border: none;
    color: rgba(var(--text-rgb), 0.5);
    cursor: pointer;
  }
  .cycle-arrow:hover { color: var(--accent-light); }
  .cycle-arrow:disabled { color: rgba(var(--text-rgb), 0.15); cursor: default; }
  .cycle-arrow:disabled:hover { color: rgba(var(--text-rgb), 0.15); }

  /* default: no cap, fits every option so nothing needs scrolling — once the
     user drags the handle below, slider.height takes over as a fixed,
     scrollable height (.capped) */
  .checklist {
    display: flex;
    flex-direction: column;
    gap: 1px;
    background: var(--grid);
    border-radius: 4px;
    padding: 4px;
  }
  .checklist.capped { overflow-y: auto; }

  .check-item {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 3px 4px;
    border-radius: 3px;
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.65);
    cursor: pointer;
    transition: background 0.1s, color 0.1s;
  }
  .check-item:hover { background: rgba(var(--accent-rgb), 0.1); color: var(--text); }
  .check-item input {
    accent-color: var(--accent);
    cursor: pointer;
    flex-shrink: 0;
  }
  .check-item span {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

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
    flex-shrink: 0;
  }
  .del:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }

  /* absolutely positioned into the row's existing bottom padding (10px, always
     reserved whether or not the handle is rendered) rather than added as a
     normal-flow sibling — otherwise the row grows by 9px the moment edit mode
     turns the handle on, and everything below it visibly jumps */
  .resize-handle {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    height: 9px;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: ns-resize;
  }
  .grip {
    width: 28px;
    height: 3px;
    border-radius: 2px;
    background: rgba(var(--text-rgb), 0.15);
    transition: background 0.15s;
  }
  .resize-handle:hover .grip, .resize-handle.resizing .grip { background: var(--accent); }
</style>
