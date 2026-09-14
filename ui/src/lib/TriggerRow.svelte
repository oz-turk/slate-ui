<script>
  import { createEventDispatcher } from 'svelte'
  import TriggerIntervalPopup from './TriggerIntervalPopup.svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, interval, intervalString, lockTargets }
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

  $: isManual = (slider.interval ?? -1000) < 0

  // ── interval popup ───────────────────────────────────────────────────────────
  let popup = null   // { x, y } | null
  function openPopup(e) {
    const rect = e.currentTarget.getBoundingClientRect()
    const w = 168, h = 260
    popup = {
      x: Math.min(rect.left, window.innerWidth  - w - 8),
      y: Math.min(rect.bottom + 4, window.innerHeight - h - 8),
    }
  }
  function onPopupChange(e) {
    dispatch('change', { kind: 'interval', interval: e.detail.interval, intervalString: e.detail.intervalString })
    popup = null
  }

  // Quick canvas-style mode flip (native ModeBox) — just negates the sign,
  // keeping the magnitude so toggling back restores the same cyclic value.
  function toggleMode() {
    const magnitude = Math.abs(slider.interval ?? 1000) || 1000
    const interval = isManual ? magnitude : -magnitude
    const intervalString = isManual ? formatMs(magnitude) : '----------'
    dispatch('change', { kind: 'interval', interval, intervalString })
  }

  function formatMs(ms) {
    if (ms < 1000) return `${ms} ms`
    if (ms < 60000) { const s = ms / 1000; return `${s} second${s === 1 ? '' : 's'}` }
    if (ms < 3600000) { const m = ms / 60000; return `${m} minute${m === 1 ? '' : 's'}` }
    const h = ms / 3600000
    return `${h} hour${h === 1 ? '' : 's'}`
  }

  function toggleLock() {
    dispatch('change', { kind: 'lock', value: !slider.lockTargets })
  }

  function fire() {
    dispatch('change', { kind: 'fire' })
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

  <!-- Empty gutter — mirrors SliderRow's "lo bound" column so the interval
       box below lines up with the slider track's own start position. -->
  <span class="gutter"></span>

  <!-- Stays within the same column SliderRow gives its track (see .row grid
       below) instead of sizing to its own text or spanning further right —
       otherwise the icons after it would shift between rows depending on how
       long each row's interval label is ("1 second" vs "----------"). -->
  <button class="interval" on:click|stopPropagation={openPopup} title="Interval">
    {slider.intervalString ?? '----------'}
  </button>

  <!-- Lucide "refresh-cw" icon (ISC license) — https://lucide.dev/icons/refresh-cw -->
  <button class="icon-btn" class:active={!isManual} on:click|stopPropagation={toggleMode}
      title={isManual ? 'Manual mode (click for cyclic)' : 'Cyclic mode (click for manual)'}>
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <path d="M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
      <path d="M3 3v5h5" />
      <path d="M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16" />
      <path d="M16 16h5v5" />
    </svg>
  </button>

  <div class="icon-pair">
    <!-- Lucide "lock"/"lock-open" icons (ISC license) — https://lucide.dev/icons/lock -->
    <button class="icon-btn" class:active={slider.lockTargets} on:click|stopPropagation={toggleLock}
        title={slider.lockTargets ? 'Targets locked (click to free)' : 'Free target objects (click to lock)'}>
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <rect width="18" height="11" x="3" y="11" rx="2" ry="2" />
        {#if slider.lockTargets}
          <path d="M7 11V7a5 5 0 0 1 10 0v4" />
        {:else}
          <path d="M7 11V7a5 5 0 0 1 9.9-1" />
        {/if}
      </svg>
    </button>

    <!-- Lucide "play" icon (ISC license) — https://lucide.dev/icons/play -->
    <button class="icon-btn play" on:click|stopPropagation={fire} title="Trigger update">
      <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" stroke="none">
        <polygon points="6 3 20 12 6 21 6 3" />
      </svg>
    </button>
  </div>

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
  {/if}
</div>

{#if popup}
  <TriggerIntervalPopup x={popup.x} y={popup.y} interval={slider.interval ?? -1000}
      on:change={onPopupChange}
      on:close={() => popup = null}
  />
{/if}

<style>
  /* Same column widths as SliderRow's grid (name / lo-bound / track / hi-bound
     / value) so a Trigger row's interval box lines up with every SliderRow's
     track above/below it in the same tab, and the icons after it land where
     SliderRow's hi-bound + value columns are. */
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
  .row.edit { grid-template-columns: 20px var(--name-col-w, 110px) 44px 1fr 44px 68px 24px; padding: 0 8px 0 6px; }
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
    padding-right: 8px;
  }

  .gutter { min-width: 0; }

  .interval {
    height: 22px;
    margin-right: 8px;
    padding: 0 10px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.65);
    font-size: 10px;
    font-family: inherit;
    text-align: left;
    cursor: pointer;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .interval:hover { color: var(--text); border-color: var(--border); }

  .icon-pair {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 6px;
  }

  .icon-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 22px;
    height: 22px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.5);
    cursor: pointer;
    flex-shrink: 0;
    padding: 0;
    justify-self: center;
  }
  .icon-btn:hover  { color: rgba(var(--text-rgb), 0.85); border-color: var(--border); }
  .icon-btn.active { background: rgba(var(--accent-rgb), 0.4); border-color: var(--accent); color: var(--text); }
  .icon-btn.play   { color: rgba(var(--text-rgb), 0.75); }
  .icon-btn.play:hover  { background: rgba(var(--accent-rgb), 0.4); border-color: var(--accent); color: var(--text); }
  .icon-btn.play:active { transform: scale(0.92); }

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
    justify-self: end;
  }
  .del:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }
</style>
