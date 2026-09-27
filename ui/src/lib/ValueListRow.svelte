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

  // Checklist mode's header only carries the name — no picker lives there
  // (see the {#if !slider.multiSelect} block below) — so an unnamed
  // checklist would otherwise waste a full 44px+margin module on a blank
  // bar. Non-checklist rows always need the header (it's the row itself:
  // name + picker + del), so this only applies to slider.multiSelect.
  $: showHeader = !slider.multiSelect || !!slider.name

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

  // Checklist header lines: an option starting with `#`/`##`/`###` (level 1-3)
  // or a bare `-` (kept for the existing convention some users already have —
  // treated as level 1) renders as a section label instead of a checkbox.
  // Row height must stay identical to a normal .check-item (see itemHeight/
  // fitChecklistHeight above) so header lines vary only in font weight/size,
  // never padding — otherwise the height-fitting math per item breaks.
  function parseChecklistHeader(opt) {
    const text = String(opt)
    const hash = text.match(/^(#{1,3})\s+(.*)$/)
    if (hash) return { level: hash[1].length, text: hash[2] }
    const dash = text.match(/^-\s+(.*)$/)
    if (dash) return { level: 1, text: dash[1] }
    return null
  }

</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected class:multi={slider.multiSelect}
    class:row-dragging={rowDragging} class:row-last={isLast}
    class:headerless={!showHeader}
    bind:this={rowEl}
    style={dragTranslateY ? `transform: translateY(${dragTranslateY}px)` : ''}
    on:click={e => mode === 'edit' && dispatch('select', { shift: e.shiftKey, ctrl: e.ctrlKey })}
    on:dragenter|preventDefault={e => e.dataTransfer.dropEffect = 'move'}
    on:dragover|preventDefault={e => rowDragOver(e, dispatch)}
    on:dragleave={e => rowDragLeave(e, dispatch)}
    on:drop|preventDefault={e => rowDrop(e, dispatch)}
    on:contextmenu|preventDefault|stopPropagation={e => dispatch('contextMenu', { x: e.clientX, y: e.clientY })}
>
  {#if mode === 'edit' && slider.multiSelect}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <!-- Checklist mode: handle lives outside the header (which is skipped
         entirely when the checklist is unnamed, see showHeader) so it never
         disappears — absolutely positioned to span the whole row (header +
         body), same idiom as PanelRow's own side handle. Doesn't touch the
         checklist's own height math, only where the handle sits visually. -->
    <div class="handle side"
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

  {#if showHeader}
  <div class="header">
    {#if mode === 'edit' && !slider.multiSelect}
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
  {/if}

  {#if slider.multiSelect}
    <!-- svelte-ignore a11y-no-static-element-interactions -->
    <div class="checklist" class:capped={slider.height != null} bind:this={checklistEl}
        style={slider.height != null ? `height: ${slider.height}px` : `min-height: ${fitChecklistHeight}px`}
        on:click|stopPropagation>
      {#each options as opt, i (i)}
        {@const header = parseChecklistHeader(opt)}
        {#if header}
          <div class="check-header level-{header.level}">{header.text}</div>
        {:else}
          <label class="check-item">
            <input type="checkbox" checked={selectedSet.has(i)} on:change={() => onToggle(i)} />
            <span>{opt}</span>
          </label>
        {/if}
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

  /* multiSelect (CheckList/Sequence) grows into a header + checklist body,
     same shape as PanelRow, instead of the single compact line */
  .row.multi {
    flex-direction: column;
    align-items: stretch;
    height: auto;
    padding: 0 12px 10px;
  }
  /* 26px = 6px base + 20px handle width — same reserve as the side handle
     below, so the header/checklist content never sits under it regardless
     of whether the checklist is named (see .handle.side). */
  .row.multi.edit { padding: 0 8px 10px 26px; }
  /* Without a header, the checklist is the row's very first thing — flush
     against the divider line above it (the previous row's .row::after).
     Matches PanelRow's own .headerless treatment for the same reason. */
  .row.multi.headerless { padding-top: 3px; }

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

  /* Checklist-only variant (see the row-level {#if} above): absolutely
     positioned so it spans the row's full height (header + body) instead of
     just the 44px header band, and survives the header being skipped when
     the checklist is unnamed. Mirrors PanelRow's own .handle exactly. */
  .handle.side {
    position: absolute;
    left: 6px;
    top: 0;
    bottom: 0;
    width: 20px;
    height: auto;
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding-top: 16px;
  }

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
  /* the arrow is baked into a data-URI SVG, so it can't read --text via var()
     like the rest of the app's icons — needs its own light-theme copy with
     the fill swapped, or it stays near-white-on-near-white in light mode */
  :global(:root[data-theme='light']) .picker {
    background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='8' height='6' viewBox='0 0 8 6'%3E%3Cpath d='M0 0L4 6L8 0Z' fill='%232a2724' fill-opacity='0.5'/%3E%3C/svg%3E");
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
    /* .checklist's overflow-y:auto (once capped, see below) resets flex
       items' automatic min-height to 0 — without this, a shrunk checklist
       squishes every row instead of just scrolling past the ones that don't
       fit. .check-item barely showed it (the checkbox's own fixed size
       already resisted collapse) but .check-header had nothing holding it
       up and would visibly compress away before it should. */
    flex-shrink: 0;
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

  /* Same box (padding/line-height) as .check-item so it doesn't disturb the
     per-item height math above — only weight/size/color vary, no checkbox
     gap since there's nothing to align to. */
  .check-header {
    padding: 3px 4px;
    font-size: 11px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    cursor: default;
    flex-shrink: 0;
  }
  .check-header.level-1 {
    font-weight: 700;
    letter-spacing: 0.02em;
    color: rgba(var(--text-rgb), 0.85);
  }
  .check-header.level-2 {
    font-weight: 600;
    color: rgba(var(--text-rgb), 0.7);
  }
  .check-header.level-3 {
    font-weight: 500;
    color: rgba(var(--text-rgb), 0.5);
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
    /* knockout outline — see SliderRow's .bound for why */
    text-shadow:
      2px 0 1.5px var(--pane-pattern-mask, transparent), -2px 0 1.5px var(--pane-pattern-mask, transparent),
      0 2px 1.5px var(--pane-pattern-mask, transparent), 0 -2px 1.5px var(--pane-pattern-mask, transparent),
      2px 2px 1.5px var(--pane-pattern-mask, transparent), -2px -2px 1.5px var(--pane-pattern-mask, transparent),
      2px -2px 1.5px var(--pane-pattern-mask, transparent), -2px 2px 1.5px var(--pane-pattern-mask, transparent);
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
