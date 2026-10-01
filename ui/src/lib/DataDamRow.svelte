<script>
  import ParamIcon from './ParamIcon.svelte'
  import { createEventDispatcher } from 'svelte'
  import DataDamModePopup from './DataDamModePopup.svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, mode, delaySeconds, delayLabel, gateLinked, gateName, gateMode }
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

  // ── mode/delay popup ─────────────────────────────────────────────────────────
  let popup = null   // { x, y } | null
  function openPopup(e) {
    const rect = e.currentTarget.getBoundingClientRect()
    // Taller than TriggerIntervalPopup's own estimate — this popup also
    // carries the "Gate Options" section (header + 2 items) below Never.
    const w = 140, h = 280
    popup = {
      x: Math.min(rect.left, window.innerWidth  - w - 8),
      y: Math.min(rect.bottom + 4, window.innerHeight - h - 8),
    }
  }
  function onPopupChange(e) {
    if (e.detail.kind === 'gateMode') {
      dispatch('change', { kind: 'gateMode', gateMode: e.detail.gateMode })
    } else {
      dispatch('change', { kind: 'mode', mode: e.detail.mode, delaySeconds: e.detail.delaySeconds, delayLabel: e.detail.delayLabel })
    }
    popup = null
  }

  function fire() {
    dispatch('change', { kind: 'fire' })
  }

  function selectGate() {
    dispatch('change', { kind: 'selectGate' })
  }

  function clearGate() {
    dispatch('change', { kind: 'clearGate' })
  }

  // Shows the linked source's own name (same "real state, not a count" idiom
  // as TriggerRow's interval label / GeometryParamRow's "N objects") — only
  // one gate can ever be linked per dam, so a count would be redundant.
  // "Gate" not "Select Gate" — the clear ("x") button next to it eats into
  // the same tight space, "Select Gate" no longer fits comfortably.
  $: gateLabel = slider.gateLinked ? (slider.gateName || 'Gated') : 'Gate'
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

  <span class="name" title={slider.name}><ParamIcon {slider} {mode} />{slider.name}</span>

  <!-- Empty gutter — mirrors SliderRow's "lo bound" column so the mode
       button below lines up with the slider track's own start position. -->
  <span class="gutter"></span>

  <button class="mode" on:click|stopPropagation={openPopup} title="Mode / Delay">
    {slider.delayLabel ?? 'Never'}
  </button>

  <div class="icon-cluster">
    <!-- Mirrors native's own two-icon swap (GH_DataDamAttributes.SetupTooltip):
         a play triangle when there's new data to send (Lucide "play", ISC
         license, https://lucide.dev/icons/play), a checkmark when the output
         is already current (Lucide "check", ISC license,
         https://lucide.dev/icons/check) — disabled in that state too, since
         datadam_fire's own TransferPossible check makes a click a no-op
         either way (see SlateWindow.cs). Undefined (not yet pushed) reads as
         "can fire" so the button isn't disabled by default. -->
    <button class="icon-btn" disabled={slider.transferPossible === false} on:click|stopPropagation={fire}
        title={slider.transferPossible === false ? 'Data is already up to date' : 'Release'}>
      {#if slider.transferPossible === false}
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M20 6 9 17l-5-5" />
        </svg>
      {:else}
        <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" stroke="none">
          <polygon points="6 3 20 12 6 21 6 3" />
        </svg>
      {/if}
    </button>

    <!-- Select Gate — links a Boolean/Integer param or Panel on the canvas
         whose value auto-releases this dam on a false→true edge (see
         data-dam-capture.md). "Select Gate" before a link exists, the
         source's own name after — the "as a gate" phrasing in both tooltips
         is what actually clarifies the action, the label alone was read as
         ambiguous ("select" what, for what). -->
    <button class="gate" class:active={slider.gateLinked} on:click|stopPropagation={selectGate}
        title={slider.gateLinked ? `Using "${slider.gateName ?? ''}" as a gate — select another object then click to replace it` : 'Select a Boolean param, Integer param, or Panel on the canvas to use as a gate, then click here'}>
      {gateLabel}
    </button>

    <!-- Lucide "x" icon (ISC license) — https://lucide.dev/icons/x, same
         glyph/style as GeometryParamRow's Clear icon. Unlinks without
         picking a replacement. -->
    <button class="icon-btn" disabled={!slider.gateLinked} on:click|stopPropagation={clearGate} title="Remove gate">
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M18 6 6 18" />
        <path d="m6 6 12 12" />
      </svg>
    </button>
  </div>

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
  {/if}
</div>

{#if popup}
  <DataDamModePopup x={popup.x} y={popup.y} mode={slider.mode ?? 'never'} delaySeconds={slider.delaySeconds ?? 0} gateMode={slider.gateMode ?? 'once'}
      on:change={onPopupChange}
      on:close={() => popup = null}
  />
{/if}

<style>
  /* Same 4-column grid as TriggerRow/GeometryParamRow, so this row's mode
     button lines up with every SliderRow's track above/below it. */
  .row {
    display: grid;
    grid-template-columns: var(--name-col-w, 110px) 44px 1fr 112px;
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
  .row.edit { grid-template-columns: 20px var(--name-col-w, 110px) 44px 1fr 112px 24px; padding: 0 8px 0 6px; }
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
    /* knockout outline — keeps the handle visible over a busy pane pattern.
       OPAQUE pane colour — text-shadow/filter colours respect the colour's
       own alpha, so a translucent outline would let the pattern bleed
       straight back through (see Pane.svelte's paneStyleCss). */
    filter:
      drop-shadow(2px 0 1.5px var(--pane-pattern-mask, transparent))
      drop-shadow(-2px 0 1.5px var(--pane-pattern-mask, transparent))
      drop-shadow(0 2px 1.5px var(--pane-pattern-mask, transparent))
      drop-shadow(0 -2px 1.5px var(--pane-pattern-mask, transparent));
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

  .mode {
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
  .mode:hover { color: var(--text); border-color: var(--border); }

  .icon-cluster {
    display: flex;
    align-items: center;
    /* flex-start — same reasoning as TriggerRow/GeometryParamRow: keeps the
       release icon flush at the same x regardless of the gate button's
       width, which varies with "Select" vs "1 gate". */
    justify-content: flex-start;
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
    color: rgba(var(--text-rgb), 0.75);
    cursor: pointer;
    flex-shrink: 0;
    padding: 0;
  }
  .icon-btn:hover  { background: rgba(var(--accent-rgb), 0.4); border-color: var(--accent); color: var(--text); }
  .icon-btn:active { transform: scale(0.92); }
  .icon-btn:disabled { opacity: 0.35; cursor: default; }
  .icon-btn:disabled:hover { background: var(--grid); border-color: var(--grid); color: rgba(var(--text-rgb), 0.75); }

  .gate {
    height: 22px;
    padding: 0 8px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.65);
    font-size: 10px;
    font-family: inherit;
    cursor: pointer;
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .gate:hover  { color: var(--text); border-color: var(--border); }
  .gate.active { background: rgba(var(--accent-rgb), 0.4); border-color: var(--accent); color: var(--text); }

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
    /* knockout outline — see SliderRow's .bound for why */
    text-shadow:
      2px 0 1.5px var(--pane-pattern-mask, transparent), -2px 0 1.5px var(--pane-pattern-mask, transparent),
      0 2px 1.5px var(--pane-pattern-mask, transparent), 0 -2px 1.5px var(--pane-pattern-mask, transparent),
      2px 2px 1.5px var(--pane-pattern-mask, transparent), -2px -2px 1.5px var(--pane-pattern-mask, transparent),
      2px -2px 1.5px var(--pane-pattern-mask, transparent), -2px 2px 1.5px var(--pane-pattern-mask, transparent);
    transition: background 0.1s, color 0.1s;
    margin-left: 4px;
    padding: 0;
    justify-self: end;
  }
  .del:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }
</style>
