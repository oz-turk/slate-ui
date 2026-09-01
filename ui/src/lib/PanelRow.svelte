<script>
  import { onMount, tick } from 'svelte'
  import { createEventDispatcher } from 'svelte'
  import { hoverHint, mode as modeStore } from '../stores/uiState.js'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  import { resizeHint, measureResizeBounds, computeResizeHeight } from './resizeHandle.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, value, readOnly, height } — value is the panel's text
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  const MODULE = 44   // one slider row's height — the app's base sizing unit

  // ── row drag (reorder) — see rowDrag.js ────────────────────────────────────────
  let rowEl

  // ── un-resized default height: fit the text, snapped up to whole MODULEs —
  // a short panel (e.g. "test1") shouldn't default to a fixed two-module
  // block. textEl.scrollHeight reflects the text's true content height
  // independent of whatever height we render the box at, so this isn't
  // circular. Only used until the user explicitly drags a height (slider.height).
  //
  // Snapping naturalHeight alone isn't enough — same issue the manual resize
  // handle already solves via resizeOverhead (see resizeHandle.js): the
  // header/margin/padding/border around the body isn't itself a multiple of
  // MODULE, so a body that's an exact multiple of MODULE can still leave the
  // ROW's outer edge off-grid. overhead is the row's own real, current chrome
  // (whatever it is right now — with or without a header), measured live off
  // rowEl like measureResizeBounds does, so `overhead + body` always lands on
  // a MODULE multiple regardless of showHeader: no fixed header-size constant
  // to keep in sync with the CSS, and no separate accounting for the
  // header/no-header cases.
  let textEl
  let naturalHeight = 0
  let overhead = 0
  // Header is only worth its 44px+margin when it's carrying something to
  // read: the name. An untitled panel would otherwise waste a full MODULE on
  // a blank bar — in edit mode too (the handle lives outside the header, see
  // .handle below, so losing the header doesn't cost it; the remove/badge
  // controls just move off until the panel is named).
  $: showHeader = !!slider.name

  async function remeasure() {
    await tick()
    if (textEl) naturalHeight = textEl.scrollHeight
    if (rowEl) overhead = rowEl.offsetHeight - bodyHeight
  }
  onMount(remeasure)
  // showHeader is included because naming/unnaming a panel changes rowEl's
  // actual chrome (the header appearing/disappearing), so a fresh measurement
  // is needed after Svelte patches the DOM.
  $: (slider.value, slider.id, showHeader, remeasure())
  // Math.max clamps the TOTAL (overhead + body) to at least one MODULE, not
  // the body alone — overhead here is already bigger than one MODULE, so the
  // floor never actually bites, but clamping the body instead would produce
  // a body/overhead sum that isn't itself a MODULE multiple, undoing the
  // whole point of this snap for the smallest sizes.
  $: fitHeight = naturalHeight
    ? Math.max(MODULE, Math.ceil((overhead + naturalHeight) / MODULE) * MODULE) - overhead
    : MODULE

  $: bodyHeight = slider.height ?? fitHeight
  let rowDragging    = false
  let dragTranslateY = 0

  // ── custom resize handle — see resizeHandle.js. custom (not native CSS
  // resize) so we control the math: capped to the pane's visible area, and
  // Ctrl snaps to whole MODULE increments ──────────────────────────────────────
  let resizing     = false
  let resizeStartY = 0
  let resizeStartH = 0
  let liveHeight   = bodyHeight
  let resizeOverhead = 0
  let maxBodyHeight  = Infinity   // capped to the pane's visible area — can't drag past the window
  let lastCtrlKey = false
  let resizeRaf = null   // see onResizeMove

  function onResizeDown(e) {
    e.preventDefault()
    e.stopPropagation()
    resizing      = true
    resizeStartY  = e.clientY
    resizeStartH  = bodyHeight
    liveHeight    = bodyHeight
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
  // liveHeight drives this row's own box directly (see .text-wrap below), so
  // it tracks the pointer with zero latency regardless of what the store is
  // doing. The 'resize' dispatch — which the parent turns into a full
  // tabs/sliders/groups clone so *other* rows can reflow around this one's
  // new height — is throttled to one per animation frame instead of one per
  // pointermove. Pointermove can fire far faster than the page can clone and
  // re-render that whole tree, and without throttling the events queue up
  // faster than Svelte can drain them: this row itself stayed smooth before
  // (nothing but liveHeight gated its own box), but everything below it was
  // rendering an increasingly stale backlog of those queued heights, which
  // reads as delayed/rubber-banding tracking and the occasional wrong-height
  // flash ("stretch") when a frame briefly shows a stale value.
  function onResizeMove(e) {
    if (!resizing) return
    lastCtrlKey = e.ctrlKey
    const { h, snapped } = computeHeight(e)
    liveHeight = h
    hoverHint.set(resizeHint(h, snapped))
    if (resizeRaf === null) {
      resizeRaf = requestAnimationFrame(() => {
        resizeRaf = null
        if (resizing) dispatch('resize', liveHeight)
      })
    }
  }
  function onResizeUp(e) {
    if (!resizing) return
    resizing = false
    if (resizeRaf !== null) { cancelAnimationFrame(resizeRaf); resizeRaf = null }
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

  function commit(e) {
    if (slider.readOnly) return
    dispatch('change', e.target.value)
  }

  // The textarea stops keydown propagation (see on:keydown below) so typing
  // never leaks into the app's global shortcuts — but that also swallows the
  // global Tab-toggles-edit/preview shortcut (App.svelte's window keydown
  // handler skips it on purpose while a text field is focused, via
  // isTextEditable, so it can't fire from here either way). Left alone, Tab
  // just falls through to the browser's native default: focus jumps to
  // whatever's next in tab order, which reads as the shortcut having no
  // effect at all. Replicate the same toggle here so Tab still does the one
  // thing it's supposed to do everywhere else in the app.
  // Plain click/drag inside the textarea is normal text editing and must
  // stay local (not toggle the row's selection). Ctrl+click is the app-wide
  // "add to selection" gesture (see Pane.svelte's onSliderSelect) — for that
  // one case we preventDefault so the browser doesn't just move focus/caret,
  // and let the event bubble to the row's on:click instead of stopping it.
  function onTextPointerDown(e) {
    if (e.ctrlKey) { e.preventDefault(); return }
    e.stopPropagation()
  }
  function onTextClick(e) {
    if (e.ctrlKey) return
    e.stopPropagation()
  }

  function onTextKeydown(e) {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault()
      commit(e)
      return
    }
    if (e.key === 'Tab') {
      e.preventDefault()
      commit(e)
      modeStore.update(m => m === 'edit' ? 'preview' : 'edit')
    }
  }

</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div class="row" data-slider-id={slider.id} class:edit={mode === 'edit'} class:selected
    class:row-dragging={rowDragging} class:row-last={isLast}
    class:headerless={!showHeader}
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
    <!-- Spans the whole row (header + body), not just the header — a
         steadier, easier-to-hit grab target than a strip inside the header
         alone, and stays put regardless of showHeader. -->
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

  {#if showHeader}
  <div class="header">
    <span class="name" title={slider.name}>{slider.name}</span>

    <div class="spacer"></div>

    {#if slider.readOnly}
      <span class="badge">read-only</span>
    {/if}

    {#if mode === 'edit'}
      <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
    {/if}
  </div>
  {/if}

  <div class="text-wrap" style="height: {resizing ? liveHeight : bodyHeight}px">
    {#if slider.readOnly}
      <div class="text-display" bind:this={textEl}>{slider.value || '—'}</div>
    {:else}
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <textarea
        class="text-input"
        bind:this={textEl}
        value={slider.value}
        placeholder="Type text… (Ctrl+Enter to apply)"
        spellcheck="false"
        on:click={onTextClick}
        on:pointerdown={onTextPointerDown}
        on:keydown|stopPropagation={onTextKeydown}
        on:change={commit}
        on:blur={commit}
      ></textarea>
    {/if}
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
</div>

<style>
  .row {
    display: flex;
    flex-direction: column;
    padding: 0 12px 3px;
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
  .row.edit            { padding: 0 8px 3px 26px; }
  /* Without a header, the recessed text box is the row's very first thing —
     flush against the divider line above it (the previous row's .row::after)
     with nothing to hold it off, unlike the bottom edge which already has
     the padding above. Named panels don't need this: the header's own 44px
     band reads as breathing room even with padding-top:0. Matches the
     bottom's 3px (see TabBar's own inter-tab gap for that reference value). */
  .row.headerless      { padding-top: 3px; }
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

  /* Absolutely positioned (like .resize-handle below) rather than a normal-
     flow flex item — it needs to span the whole row (header + body) as a
     grab target, not just sit inside the header. left:6px + 20px width
     mirrors the other row types' left:6px padding + 20px handle column (see
     e.g. SliderRow's `.row.edit`), so the grip sits at the same horizontal
     position everywhere instead of drifting left; .row's own left padding
     (26px in edit mode, see .row.edit above = that 6px + this 20px) reserves
     exactly the room for it. The grip icon itself is pinned near the top
     (padding-top centers it within the 44px header band) rather than
     centered over the whole span — centering over the full height put it
     noticeably lower on any panel whose body is taller than one module
     (resized, or just multi-line text), since the body's height dominates
     the average. */
  .handle {
    position: absolute;
    left: 6px;
    top: 0;
    bottom: 0;
    width: 20px;
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding-top: 16px;
    color: rgba(var(--text-rgb), 0.21);
    cursor: grab;
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
