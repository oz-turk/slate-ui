<script>
  import ParamIcon from './ParamIcon.svelte'
  import { createEventDispatcher } from 'svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  import { fullscreenTreeId } from '../stores/uiState.js'
  import { totalCount, branchCount } from './treeLayout.js'
  import { commitReorder } from './mapperActions.js'
  import PathMapperPanel from './PathMapperPanel.svelte'
  import SectionHead from './SectionHead.svelte'
  import DataTreeSunburst from './DataTreeSunburst.svelte'
  import GraphControls from './GraphControls.svelte'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, tree, paramViewerSize?, showCounts?, source?, mapping?, mapperOrders?, mapperOpen?, graphOpen? }
  export let mode      = 'preview'
  export let selected  = false
  export let isFirst   = false
  export let isLast    = false

  // Inline preview below the header bar — a fixed size, not one that
  // reflows with the pane (2026-09-26 kullanıcı: "sabit bir component
  // yüksekliği olması istiyorum"). Computed once by hand, not measured at
  // runtime: Slate's own default window width (380, SlateWindow.cs
  // DefaultWindowSize) rounded DOWN to the nearest MODULE (44) — 380/44 =
  // 8.64, floor → 8 × 44 = 352 (ceiling would give 396, wider than the
  // window itself, kullanıcı: "kenarlar sığmayacak galiba"). Same value for
  // both width and height since the sunburst is square.
  const MODULE = 44
  const AUTO_SIZE = 352
  // Per-capture override (2026-09-26 kullanıcı: "bazı tree'lerde daha büyük
  // olmak isteyebilir" — a deep/wide tree needs more room than the shared
  // default). null means "use AUTO_SIZE"; set once the +/- stepper in
  // GraphControls is used, persisted on the slider like Text
  // Panel's textFontSize etc.
  $: sunburstSize = slider.paramViewerSize ?? AUTO_SIZE
  // "(N = X)" on the graph — default ON (2026-09-26 kullanıcı: "default
  // açık olsun"), so only an explicit `false` turns it off.
  $: showCounts = slider.showCounts !== false

  // Same row type for a captured Param Viewer AND a captured Path Mapper
  // (2026-10-04 kullanıcı: "path mapper'ı capture ettiğimde de aynı graph'ı
  // görelim"). Viewer: graph is the point, no mapper section, read-only.
  // Mapper: mapper section on top (open by default), graph below collapsed
  // by default — but openable, since the radial reorder gesture lives on it.
  $: isMapper   = slider.source === 'mapper'
  $: graphOpen  = isMapper ? slider.graphOpen === true : true
  $: manual     = slider.mapperManual === true
  function toggleGraph()  { dispatch('patch', { graphOpen: !graphOpen }) }

  function onReorder(e) { dispatch('patch', { mapperOrders: commitReorder(slider, e.detail) }) }

  function onSizeChange(e)       { dispatch('paramViewerSize', e.detail) }
  function onShowCountsChange(e) { dispatch('paramViewerShowCounts', e.detail) }

  // ── row drag (reorder within the pane's own list) — see rowDrag.js ─────────────
  let rowEl
  let rowDragging    = false
  let dragTranslateY = 0

  function onHandleDrag(e) {
    const v = dragTranslateYFor(e, rowEl, isFirst, isLast)
    if (v !== null) dragTranslateY = v
  }

  $: branches = slider.tree ? branchCount(slider.tree) : 0
  $: items    = slider.tree ? totalCount(slider.tree) : 0

  function openFullscreen() {
    fullscreenTreeId.set(slider.id)
  }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div class="pv-block" data-slider-id={slider.id} class:row-dragging={rowDragging} class:row-last={isLast}
    style={dragTranslateY ? `transform: translateY(${dragTranslateY}px)` : ''}
    on:dragenter|preventDefault={e => e.dataTransfer.dropEffect = 'move'}
    on:dragover|preventDefault={e => rowDragOver(e, dispatch)}
    on:dragleave={e => rowDragLeave(e, dispatch)}
    on:drop|preventDefault={e => rowDrop(e, dispatch)}
    on:contextmenu|preventDefault|stopPropagation={e => dispatch('contextMenu', { x: e.clientX, y: e.clientY })}
>
  <!-- svelte-ignore a11y-no-static-element-interactions -->
  <!-- svelte-ignore a11y-click-events-have-key-events -->
  <div class="row" class:edit={mode === 'edit'} class:selected
      bind:this={rowEl}
      on:click={e => mode === 'edit' && dispatch('select', { shift: e.shiftKey, ctrl: e.ctrlKey })}
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

    <span class="name" class:wide={isMapper} title={slider.name}><ParamIcon {slider} {mode} />{slider.name}</span>

    <span class="summary">{branches} branch{branches === 1 ? '' : 'es'} · {items} item{items === 1 ? '' : 's'}</span>

    <!-- Fullscreen + remove (×) as one tight cluster (4px apart, like the other
         rows' ×) — the .row's own 10px flex gap used to push them 20px apart. -->
    <div class="cluster">
    <button class="expand" on:click|stopPropagation={openFullscreen} title="Open in fullscreen">
      <!-- Lucide "maximize-2" icon (ISC license) — https://lucide.dev/icons/maximize-2 -->
      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M15 3h6v6" /><path d="M9 21H3v-6" /><path d="M21 3l-7 7" /><path d="M3 21l7-7" />
      </svg>
    </button>

    {#if mode === 'edit'}
      <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
    {/if}
    </div>
  </div>

  {#if isMapper}
    <PathMapperPanel {slider} {mode} on:patch />
    <SectionHead label="Graph" open={graphOpen} on:click={toggleGraph} />
  {/if}

  {#if slider.tree && graphOpen}
    <div class="body" style="height:{sunburstSize}px">
      <DataTreeSunburst tree={slider.tree} size={sunburstSize} id={slider.sourceId ?? slider.id} {showCounts}
        reorderable={isMapper && !manual} on:reorder={onReorder} />
      {#if mode === 'edit'}
        <GraphControls size={sunburstSize} {showCounts}
          on:sizeChange={onSizeChange}
          on:showCountsChange={onShowCountsChange} />
      {/if}
    </div>
  {/if}
</div>

<style>
  .pv-block {
    position: relative;
    transition: transform 0.08s ease-out;
  }
  .pv-block:not(.row-last)::after {
    content: '';
    position: absolute;
    left: 12px;
    right: 12px;
    bottom: 0;
    height: 1px;
    background: var(--edge-tint);
    pointer-events: none;
  }
  .pv-block.row-dragging { opacity: 0.5; position: relative; z-index: 2; }

  .row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 0 12px;
    height: 44px;
    transition: background 0.1s;
  }
  .row.edit { padding: 0 8px 0 6px; }
  .row:hover          { background: var(--pane-pattern-mask, transparent); }
  .row.selected       { background: rgba(var(--accent-rgb), 0.15); }
  .row.selected:hover { background: rgba(var(--accent-rgb), 0.22); }

  .body {
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    position: relative;
  }

  /* Shared box for every row-level icon button (settings, fullscreen,
     remove) — 2026-09-27 kullanıcı: "iconların arka plan renkleri ve
     boyutları da tutarlı olsun". Opaque background (not just a border) so
     each one also fully occludes whatever's behind it without needing a
     separate knockout-shadow trick. */
  .cluster { display: flex; align-items: center; gap: 4px; flex-shrink: 0; }

  .expand {
    width: 24px;
    height: 24px;
    display: flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 4px;
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.5);
    cursor: pointer;
    padding: 0;
    flex-shrink: 0;
    transition: background 0.1s, color 0.15s;
  }
  .expand:hover { background: var(--border); color: rgba(var(--text-rgb), 0.85); }

  /* Same remove × as every other row (20px, unboxed, very faint until hover) —
     2026-10-04 kullanıcı: "box'a gerek yok, diğer componentlardaki gibi". */
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
    flex-shrink: 0;
    padding: 0;
    text-shadow:
      2px 0 1.5px var(--pane-pattern-mask, transparent), -2px 0 1.5px var(--pane-pattern-mask, transparent),
      0 2px 1.5px var(--pane-pattern-mask, transparent), 0 -2px 1.5px var(--pane-pattern-mask, transparent),
      2px 2px 1.5px var(--pane-pattern-mask, transparent), -2px -2px 1.5px var(--pane-pattern-mask, transparent),
      2px -2px 1.5px var(--pane-pattern-mask, transparent), -2px 2px 1.5px var(--pane-pattern-mask, transparent);
    transition: background 0.1s, color 0.1s;
  }
  .del:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }

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
  /* .row is flex with gap: 10px (the other rows are grid, gap 0) — cancel the
     gap after the handle so the name column starts at the same x as theirs. */
  .row.edit .handle { margin-right: -10px; }

  .name {
    flex: 0 0 var(--name-col-w, 110px);
    font-size: 12px;
    font-weight: 500;
    color: rgba(var(--text-rgb), 0.85);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  /* A Path Mapper row has nothing else beside its title, so it isn't held to
     the shared name column (110px truncated "Path Mapp...") — it takes what it
     needs and only shrinks (with an ellipsis) when the summary needs room too
     (2026-10-04 kullanıcı). */
  .name.wide { flex: 0 1 auto; min-width: 0; }

  .summary {
    flex: 1 1 auto;
    min-width: 0;
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.5);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-variant-numeric: tabular-nums;
  }

</style>
