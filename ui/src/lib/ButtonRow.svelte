<script>
  import { createEventDispatcher } from 'svelte'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, value } — value is 0 (up) or 1 (pressed)
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  // ── row drag (reorder) — mirrors SliderRow's mechanism ────────────────────────
  let rowEl
  let rowDragging    = false
  let dragTranslateY = 0

  function onHandleDrag(e) {
    if (e.clientX === 0 && e.clientY === 0) return
    if (!rowEl) return
    const rect = rowEl.getBoundingClientRect()
    const cap  = 14
    if      (isLast  && e.clientY > rect.bottom) dragTranslateY =  Math.min(e.clientY - rect.bottom, cap)
    else if (isFirst && e.clientY < rect.top)    dragTranslateY = -Math.min(rect.top - e.clientY, cap)
    else                                          dragTranslateY = 0
  }

  // ── press / release (momentary, not a toggle) ─────────────────────────────────
  let pressed = false

  function onPress(e) {
    e.preventDefault()
    if (pressed) return
    pressed = true
    e.currentTarget.setPointerCapture(e.pointerId)
    dispatch('change', 1)
  }
  function onRelease(e) {
    if (!pressed) return
    pressed = false
    dispatch('change', 0)
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

  <!-- svelte-ignore a11y-no-noninteractive-element-interactions -->
  <button
    class="press"
    class:pressed
    aria-label={slider.name}
    on:click|stopPropagation
    on:pointerdown|stopPropagation={onPress}
    on:pointerup={onRelease}
    on:pointercancel={onRelease}
  >
    Press
  </button>

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
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

  .press {
    height: 24px;
    padding: 0 14px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.65);
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
    flex-shrink: 0;
    user-select: none;
    transition: background 0.08s, border-color 0.08s, color 0.08s, transform 0.08s;
  }
  .press.pressed {
    background: rgba(var(--accent-rgb), 0.4);
    border-color: var(--accent);
    color: var(--text);
    transform: scale(0.95);
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
  }
  .del:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }
</style>
