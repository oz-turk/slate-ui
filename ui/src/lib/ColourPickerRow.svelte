<script>
  import { createEventDispatcher } from 'svelte'
  import ColourPickerPopup from './ColourPickerPopup.svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, value } — value is an "#rrggbbaa" hex string
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

  // ── popup ─────────────────────────────────────────────────────────────────────
  let popup = null   // { x, y } | null
  function openPopup(e) {
    const rect = e.currentTarget.getBoundingClientRect()
    const w = 216, h = 300
    popup = {
      x: Math.min(rect.left, window.innerWidth  - w - 8),
      y: Math.min(rect.bottom + 4, window.innerHeight - h - 8),
    }
  }
  function onPopupChange(e) {
    dispatch('change', e.detail)
  }

  $: hexDisplay = (slider.value ?? '').slice(1, 7)
  $: alphaByte  = parseInt((slider.value ?? '').slice(7, 9), 16)
  $: alphaPct   = isNaN(alphaByte) ? 100 : Math.round(alphaByte / 255 * 100)

  // A flat-colour layer over a checkerboard — background-color would sit
  // *behind* the checkerboard image and never show through, so the colour
  // has to be its own background-image layer instead. Built as one inline
  // style string (not the `background` shorthand) so each layer keeps its
  // own background-size — the shorthand would reset the checker tiling.
  $: swatchStyle = `background-image: linear-gradient(${slider.value}, ${slider.value}),
      linear-gradient(45deg, #4a4a4a 25%, transparent 25%),
      linear-gradient(-45deg, #4a4a4a 25%, transparent 25%),
      linear-gradient(45deg, transparent 75%, #4a4a4a 75%),
      linear-gradient(-45deg, transparent 75%, #4a4a4a 75%);
    background-size: 100% 100%, 8px 8px, 8px 8px, 8px 8px, 8px 8px;
    background-position: 0 0, 0 0, 0 4px, 4px -4px, -4px 0;
    background-color: #2a2a2a;`

</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected
    class:row-dragging={rowDragging}
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

  <div class="spacer"></div>

  <span class="hex">{hexDisplay}{alphaPct < 100 ? `  ${alphaPct}%` : ''}</span>
  <button class="swatch" style={swatchStyle} title="Edit colour" on:click|stopPropagation={openPopup}></button>

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
  {/if}
</div>

{#if popup}
  <ColourPickerPopup x={popup.x} y={popup.y} hex={slider.value}
      on:change={onPopupChange}
      on:close={() => popup = null}
  />
{/if}

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

  .hex {
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.43);
    margin-right: 8px;
    flex-shrink: 0;
  }

  .swatch {
    width: 24px;
    height: 20px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    cursor: pointer;
    flex-shrink: 0;
    display: block;
    overflow: hidden;
    position: relative;
    padding: 0;
  }
  .swatch:hover { border-color: var(--border); }

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
