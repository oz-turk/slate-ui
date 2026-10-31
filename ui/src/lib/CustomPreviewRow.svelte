<script>
  import ParamIcon from './ParamIcon.svelte'
  import { createEventDispatcher } from 'svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, preview, render, enabled, filter }
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

  // Preview / Render / Enabled mirror the native Custom Preview context menu
  // items one to one; each is a plain on/off patched locally and sent to C#.
  // Undefined (not yet pushed) reads as on, matching the native defaults.
  $: previewOn = slider.preview !== false
  $: renderOn  = slider.render  !== false
  $: enabledOn = slider.enabled !== false

  function toggle(field, current) {
    dispatch('change', { field, value: !current })
  }

  // Filter is text: commit on Enter / blur only (not per keystroke), so a
  // half-typed pattern never briefly hides the geometry in some viewport.
  let filterValue = slider.filter ?? ''
  let filterFocused = false
  $: if (!filterFocused) filterValue = slider.filter ?? ''
  function commitFilter() {
    filterFocused = false
    const v = filterValue.trim()
    if (v !== (slider.filter ?? '')) dispatch('change', { field: 'filter', value: v })
  }
  function onFilterKey(e) {
    e.stopPropagation()
    if (e.key === 'Enter')  e.currentTarget.blur()
    if (e.key === 'Escape') { filterValue = slider.filter ?? ''; e.currentTarget.blur() }
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

  <!-- Empty gutter — same 44px column as GeometryParamRow/DataDamRow, so the
       filter box starts and ends where their pick/mode buttons do. -->
  <span class="gutter"></span>

  <!-- Viewport Filter: wildcard pattern matched against viewport names
       (empty or * = every viewport), same field as the native menu's text box. -->
  <input class="filter" type="text" spellcheck="false"
      placeholder="All viewports"
      title="Viewport filter — wildcard pattern matched against viewport names (e.g. Perspective, Top, P*). Empty = every viewport."
      bind:value={filterValue}
      on:focus={() => filterFocused = true}
      on:blur={commitFilter}
      on:keydown={onFilterKey}
      on:click|stopPropagation />

  <div class="icon-cluster">
    <!-- Preview: viewport drawing on/off (Lucide "eye"/"eye-off", ISC license) -->
    <button class="icon-btn" class:active={previewOn} on:click|stopPropagation={() => toggle('preview', previewOn)}
        title={previewOn ? 'Preview on — click to hide in viewports' : 'Preview off — click to show in viewports'}>
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        {#if previewOn}
          <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8Z" />
          <circle cx="12" cy="12" r="3" />
        {:else}
          <path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49" />
          <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242" />
          <path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143" />
          <path d="m2 2 20 20" />
        {/if}
      </svg>
    </button>

    <!-- Render: include in Rendered/Raytraced display and renders (Lucide "camera"/"camera-off", ISC license) -->
    <button class="icon-btn" class:active={renderOn} on:click|stopPropagation={() => toggle('render', renderOn)}
        title={renderOn ? 'Render on — included in renders, click to exclude' : 'Render off — excluded from renders, click to include'}>
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        {#if renderOn}
          <path d="M13.997 4a2 2 0 0 1 1.76 1.05l.486.9A2 2 0 0 0 18.003 7H20a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2h1.997a2 2 0 0 0 1.759-1.048l.489-.904A2 2 0 0 1 10.004 4z" />
          <circle cx="12" cy="13" r="3" />
        {:else}
          <path d="M14.564 14.558a3 3 0 1 1-4.122-4.121" />
          <path d="m2 2 20 20" />
          <path d="M20 20H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2h1.997a2 2 0 0 0 .819-.175" />
          <path d="M9.695 4.024A2 2 0 0 1 10.004 4h3.993a2 2 0 0 1 1.76 1.05l.486.9A2 2 0 0 0 18.003 7H20a2 2 0 0 1 2 2v7.344" />
        {/if}
      </svg>
    </button>

    <!-- Bake: opens GH's own "Bake..." dialog (layer picker) for this component. Unlike
         a plain param bake it also bakes each item's material. Same Lucide "egg-fried" icon
         (ISC license) as the geometry param's bake button (GeometryParamRow.svelte). -->
    <button class="icon-btn action" on:click|stopPropagation={() => dispatch('change', { field: 'bake' })}
        title="Bake… — bakes the previewed geometry with its materials">
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="11.5" cy="12.5" r="3.5" />
        <path d="M3 8c0-3.5 2.5-6 6.5-6 5 0 4.83 3 7.5 5s5 2 5 6c0 4.5-2.5 6.5-7 6.5-2.5 0-2.5 2.5-6 2.5s-7-2-7-5.5c0-3 1.5-3 1.5-5C3.5 10 3 9 3 8Z" />
      </svg>
    </button>

    <!-- Enabled: component solves or not (Lucide "power", ISC license) -->
    <button class="icon-btn" class:active={enabledOn} on:click|stopPropagation={() => toggle('enabled', enabledOn)}
        title={enabledOn ? 'Enabled — click to disable the component' : 'Disabled — click to enable the component'}>
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M12 2v10" />
        <path d="M18.4 6.6a9 9 0 1 1-12.77.04" />
      </svg>
    </button>
  </div>

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
  {/if}
</div>

<style>
  /* name | filter (1fr) | 3 icon buttons — same name column / left edge as
     TriggerRow/GeometryParamRow/DataDamRow so rows line up in a list. */
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

  .filter {
    height: 22px;
    min-width: 0;
    margin-right: 8px;
    padding: 0 8px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    background: var(--grid);
    color: var(--text);
    font-size: 10px;
    font-family: inherit;
    outline: none;
    box-sizing: border-box;
  }
  .filter::placeholder { color: rgba(var(--text-rgb), 0.36); }
  .filter:hover { border-color: var(--border); }
  .filter:focus { border-color: var(--accent); }

  .icon-cluster {
    display: flex;
    align-items: center;
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
    color: rgba(var(--text-rgb), 0.4);
    cursor: pointer;
    flex-shrink: 0;
    padding: 0;
  }
  .icon-btn.active { background: rgba(var(--accent-rgb), 0.4); border-color: var(--accent); color: var(--text); }
  .icon-btn:hover  { color: var(--text); border-color: var(--border); }
  .icon-btn:not(.active):hover { color: var(--text); }
  .icon-btn.active:hover { border-color: var(--accent); }
  .icon-btn:active { transform: scale(0.92); }

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
