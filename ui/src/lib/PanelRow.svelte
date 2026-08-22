<script>
  import { createEventDispatcher } from 'svelte'
  import { hoverHint } from '../stores/uiState.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, value, readOnly, height } — value is the panel's text
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  const MODULE = 44   // one slider row's height — the app's base sizing unit
  $: bodyHeight = slider.height ?? MODULE * 2

  // ── row drag (reorder) — mirrors SliderRow's mechanism ────────────────────────
  let rowEl
  let rowDragging    = false
  let dragTranslateY = 0

  // ── custom resize handle — custom (not native CSS resize) so we control the
  // math: capped to the pane's visible area, and Ctrl snaps to whole MODULE increments ─
  let resizing     = false
  let resizeStartY = 0
  let resizeStartH = 0
  let liveHeight   = bodyHeight

  function resizeHint(h, snapped) {
    return `${h}px${snapped ? ' (snapped)' : ''}  ·  Hold Ctrl to snap to slider-row size`
  }

  // header/margin/handle/padding/border around the resizable body — not itself
  // a multiple of MODULE, so snapping bodyHeight alone can never land the
  // row's outer edge on the grid. Measured live (not hardcoded) so it keeps
  // working if the surrounding layout ever changes.
  let resizeOverhead = 0
  let maxBodyHeight  = Infinity   // capped to the pane's visible area — can't drag past the window

  function onResizeDown(e) {
    e.preventDefault()
    e.stopPropagation()
    resizing      = true
    resizeStartY  = e.clientY
    resizeStartH  = bodyHeight
    liveHeight    = bodyHeight
    lastCtrlKey   = false
    resizeOverhead = rowEl ? rowEl.offsetHeight - bodyHeight : 0
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

  function commit(e) {
    if (slider.readOnly) return
    dispatch('change', e.target.value)
  }

  function onTextKeydown(e) {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault()
      commit(e)
    }
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
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected
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

    {#if slider.readOnly}
      <span class="badge">read-only</span>
    {/if}

    {#if mode === 'edit'}
      <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
    {/if}
  </div>

  <div class="text-wrap" style="height: {bodyHeight}px">
    {#if slider.readOnly}
      <div class="text-display">{slider.value || '—'}</div>
    {:else}
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <textarea
        class="text-input"
        value={slider.value}
        placeholder="Type text… (Ctrl+Enter to apply)"
        on:click|stopPropagation
        on:pointerdown|stopPropagation
        on:keydown|stopPropagation={onTextKeydown}
        on:change={commit}
        on:blur={commit}
      ></textarea>
    {/if}
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
</div>

<style>
  .row {
    display: flex;
    flex-direction: column;
    padding: 0 12px 10px;
    border-bottom: 1px solid var(--grid);
    transition: background 0.1s, transform 0.08s ease-out;
    position: relative;
  }
  .row.edit            { padding: 0 8px 10px 6px; }
  .row:hover          { background: var(--bg); }
  .row.selected       { background: rgba(var(--accent-rgb), 0.15); }
  .row.selected:hover { background: rgba(var(--accent-rgb), 0.22); }
  .row.row-dragging   { opacity: 0.5; position: relative; z-index: 2; }

  .header {
    display: flex;
    align-items: center;
    height: 44px;
    margin-bottom: 4px;
  }

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

  .badge {
    font-size: 9px;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgba(var(--text-rgb), 0.36);
    flex-shrink: 0;
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

  /* code-block look: monospace, recessed fill, thin accent rule on the left —
     no height cap here, .text-wrap's inline height (module-based) governs it */
  .text-wrap { width: 100%; }
  .text-display, .text-input {
    width: 100%;
    height: 100%;
    box-sizing: border-box;
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 11px;
    background: var(--grid);
    border: 1px solid transparent;
    border-left: 2px solid rgba(var(--text-rgb), 0.15);
    border-radius: 4px;
    padding: 6px 8px;
    overflow-y: auto;
  }

  .text-display {
    color: rgba(var(--text-rgb), 0.75);
    white-space: pre-wrap;
    word-break: break-word;
  }

  .text-input {
    color: var(--text);
    resize: none;
    outline: none;
    transition: border-color 0.15s;
  }
  .text-input:focus { border-left-color: var(--accent); }

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
