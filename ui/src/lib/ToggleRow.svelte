<script>
  import { createEventDispatcher } from 'svelte'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, value } — value is 0 or 1
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

  function toggle() {
    dispatch('change', slider.value ? 0 : 1)
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
    class="switch"
    class:on={!!slider.value}
    role="switch"
    aria-checked={!!slider.value}
    aria-label={slider.name}
    on:click|stopPropagation={toggle}
  >
    <span class="knob"></span>
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
