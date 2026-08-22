<script>
  import { createEventDispatcher } from 'svelte'
  import { hoverHint } from '../stores/uiState.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, value, options, multiSelect }
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  $: options     = slider.options ?? []
  $: selectedSet = new Set(slider.multiSelect && Array.isArray(slider.value) ? slider.value : [])

  const MODULE = 44   // one slider row's height — the app's base sizing unit

  // ── row drag (reorder) — mirrors SliderRow's mechanism ────────────────────────
  let rowEl
  let rowDragging    = false
  let dragTranslateY = 0

  // ── checklist resize — default is unset (fits every option, no scroll);
  // once dragged, slider.height takes over as an explicit, scrollable height.
  // Same custom handle as PanelRow: capped to the pane's visible area, Ctrl
  // snaps to MODULE increments.
  let resizing     = false
  let resizeStartY = 0
  let resizeStartH = 0
  let liveHeight   = 0

  function resizeHint(h, snapped) {
    return `${h}px${snapped ? ' (snapped)' : ''}  ·  Hold Ctrl to snap to slider-row size`
  }

  // header/margin/handle/padding/border around the resizable checklist body —
  // not itself a multiple of MODULE, so snapping the body alone can never land
  // the row's outer edge on the grid. Measured live (not hardcoded) so it
  // keeps working if the surrounding layout ever changes.
  let resizeOverhead = 0
  let maxBodyHeight  = Infinity   // capped to the pane's visible area — can't drag past the window

  function onResizeDown(e) {
    e.preventDefault()
    e.stopPropagation()
    resizing      = true
    resizeStartY  = e.clientY
    resizeStartH  = slider.height ?? e.currentTarget.previousElementSibling?.offsetHeight ?? MODULE * 3
    liveHeight    = resizeStartH
    lastCtrlKey   = false
    resizeOverhead = rowEl ? rowEl.offsetHeight - resizeStartH : 0
    const contentEl = rowEl?.closest('.content')
    maxBodyHeight = contentEl && rowEl
      ? contentEl.getBoundingClientRect().bottom - rowEl.getBoundingClientRect().top - resizeOverhead
      : Infinity
    hoverHint.set(resizeHint(liveHeight, false))
    dispatch('resizeStart', slider.id)
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  let lastCtrlKey = false
  function computeHeight(e, snapped = e.ctrlKey) {
    let h = resizeStartH + (e.clientY - resizeStartY)
    if (snapped) h = Math.round((h + resizeOverhead) / MODULE) * MODULE - resizeOverhead
    h = Math.max(MODULE, Math.round(h))
    h = Math.min(h, Math.max(MODULE, Math.round(maxBodyHeight)))
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
    if (e.clientX === 0 && e.clientY === 0) return
    if (!rowEl) return
    const rect = rowEl.getBoundingClientRect()
    const cap  = 14
    if      (isLast  && e.clientY > rect.bottom) dragTranslateY =  Math.min(e.clientY - rect.bottom, cap)
    else if (isFirst && e.clientY < rect.top)    dragTranslateY = -Math.min(rect.top - e.clientY, cap)
    else                                          dragTranslateY = 0
  }

  function onSelect(e) {
    dispatch('change', +e.target.value)
  }

  function onToggle(i) {
    dispatch('change', i)
  }

  function rowDragOver(e) {
    e.dataTransfer.dropEffect = 'move'
    const rect = e.currentTarget.getBoundingClientRect()
    dispatch('rowDragOver', e.clientY < rect.top + rect.height / 2 ? 'before' : 'after')
  }

  function rowDragLeave(e) {
    if (!e.currentTarget.contains(e.relatedTarget)) dispatch('rowDragLeave')
  }

  function rowDrop(e) {
    const rect = e.currentTarget.getBoundingClientRect()
    dispatch('rowDrop', e.clientY < rect.top + rect.height / 2 ? 'before' : 'after')
  }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected class:multi={slider.multiSelect}
    class:row-dragging={rowDragging}
    bind:this={rowEl}
    style={dragTranslateY ? `transform: translateY(${dragTranslateY}px)` : ''}
    on:click={e => mode === 'edit' && dispatch('select', e.shiftKey || e.ctrlKey)}
    on:dragenter|preventDefault={e => e.dataTransfer.dropEffect = 'move'}
    on:dragover|preventDefault={rowDragOver}
    on:dragleave={rowDragLeave}
    on:drop|preventDefault={rowDrop}
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

    {#if !slider.multiSelect}
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
    <div class="checklist" class:capped={slider.height != null} style={slider.height != null ? `height: ${slider.height}px` : ''} on:click|stopPropagation>
      {#each options as opt, i (i)}
        <label class="check-item">
          <input type="checkbox" checked={selectedSet.has(i)} on:change={() => onToggle(i)} />
          <span>{opt}</span>
        </label>
      {/each}
    </div>

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
</div>

<style>
  .row {
    display: flex;
    align-items: center;
    gap: 0;
    padding: 0 12px;
    height: 44px;
    border-bottom: 1px solid var(--grid);
    transition: background 0.1s, transform 0.08s ease-out;
    position: relative;
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

  .resize-handle {
    height: 9px;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: ns-resize;
    flex-shrink: 0;
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
