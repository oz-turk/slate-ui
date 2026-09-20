<script>
  import { createEventDispatcher } from 'svelte'
  import { dragTranslateYFor, rowDragOver, rowDragLeave, rowDrop } from './rowDrag.js'
  const dispatch = createEventDispatcher()

  export let slider    = {}   // { id, name, geomKind, count, internalize }
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

  const KIND_LABELS = {
    point: 'Point', curve: 'Curve', brep: 'Brep', mesh: 'Mesh', surface: 'Surface', subd: 'SubD', box: 'Box', geometry: 'Geometry',
    // Extended (Rhino 8-only) kinds — see EnsureExtendedGeometryReflection in SlateWindow.cs
    extrusion: 'Extrusion', pointcloud: 'Point Cloud', hatch: 'Hatch', textentity: 'Text Entity', centermark: 'Centermark',
    leader: 'Leader', light: 'Light', blockinstance: 'Block Instance', lineardimension: 'Linear Dimension',
    angulardimension: 'Angular Dimension', ordinatedimension: 'Ordinate Dimension', radialdimension: 'Radial Dimension',
  }
  $: kindLabel = KIND_LABELS[slider.geomKind] ?? 'Geometry'
  $: count     = slider.count ?? 0
  $: pickLabel = count > 0 ? `${count} object${count === 1 ? '' : 's'}` : `Set ${kindLabel}`

  function pick() {
    dispatch('change', { kind: 'pick', internalize: !!slider.internalize })
  }

  function toggleInternalize() {
    dispatch('change', { kind: 'internalize', value: !slider.internalize })
  }

  function clearValues() {
    dispatch('change', { kind: 'clear' })
  }

  function bake() {
    dispatch('change', { kind: 'bake' })
  }

  // Per-item "always show on GH canvas" — see PreviewPinConduit. Only actually
  // draws when the enclosing group (if any) isn't itself hidden — a group's
  // own eye toggle (GroupSection.svelte) is a gate over this, same as a
  // Blender collection's visibility gates its objects': group hidden → nothing
  // under it shows regardless of this flag; group visible → this flag decides.
  function togglePin() {
    dispatch('change', { kind: 'pin', value: !slider.previewPinned })
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

  <!-- Empty gutter — mirrors SliderRow's "lo bound" column, same reasoning
       as TriggerRow's, so this row's pick button lines up with every
       SliderRow's track above/below it in the same tab. -->
  <span class="gutter"></span>

  <button class="pick" on:click|stopPropagation={pick} title={`Pick ${kindLabel.toLowerCase()} geometry from Rhino`}>
    {pickLabel}
  </button>

  <!-- TriggerRow spends its own 4th column (44px) on one icon-btn and its 5th
       (68px) on a 2-icon flex pair; this row needs 4 icons total, so instead
       of following that same 44/68 split (which is what produced the visible
       gap here before — Clear alone in the 44px slot, then a gap before the
       pair) both columns are merged below into one 112px slot holding all 4
       contiguously. Combined pixel width is unchanged (44+68 == 112px), so
       the 1fr pick/track column before it stays exactly as wide as
       SliderRow's/TriggerRow's — only the internal split of the trailing
       fixed space changes, not the shared track alignment. -->
  <div class="icon-cluster">
    <!-- Lucide "x" icon (ISC license) — https://lucide.dev/icons/x -->
    <button class="icon-btn" disabled={count === 0} on:click|stopPropagation={clearValues} title="Clear captured values">
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M18 6 6 18" />
        <path d="m6 6 12 12" />
      </svg>
    </button>

    <!-- Lucide "eye"/"eye-off" (ISC license) — https://lucide.dev/icons/eye,
         https://lucide.dev/icons/eye-off. Mirrors the group-level pin icon
         in GroupSection.svelte, same meaning at the individual-item level. -->
    <button class="icon-btn" class:active={slider.previewPinned} on:click|stopPropagation={togglePin}
        title={slider.previewPinned ? 'Always shown on GH canvas — click to unpin' : 'Always show this on the GH canvas, even when GH is set to only preview selected objects'}>
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8Z" />
        <circle cx="12" cy="12" r="3" />
      </svg>
    </button>

    <!-- Live-reference state: Lucide "link-2" (ISC license), used as-is —
         https://lucide.dev/icons/link-2. Internalized state: a custom
         composite ("lock-pin" in NOTICE.md) — Lucide's "lock" body with an
         added keyhole slot, Lucide has no dedicated baked-geometry icon. -->
    <button class="icon-btn" class:active={slider.internalize} on:click|stopPropagation={toggleInternalize}
        title={slider.internalize ? 'Internalized — baked into file (click to keep a live Rhino reference)' : 'Live Rhino reference (click to internalize — bake into file)'}>
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        {#if slider.internalize}
          <rect width="18" height="11" x="3" y="11" rx="2" ry="2" />
          <path d="M12 15v2" />
          <path d="M7 11V7a5 5 0 0 1 10 0v4" />
        {:else}
          <path d="M9 17H7A5 5 0 0 1 7 7h2" />
          <path d="M15 7h2a5 5 0 1 1 0 10h-2" />
          <line x1="8" x2="16" y1="12" y2="12" />
        {/if}
      </svg>
    </button>

    <!-- Lucide "egg-fried" icon (ISC license) — https://lucide.dev/icons/egg-fried,
         used to stand in for "bake to Rhino" (no dedicated bake icon in Lucide). -->
    <button class="icon-btn" disabled={count === 0} on:click|stopPropagation={bake} title="Bake to Rhino (opens GH's own layer picker)">
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="11.5" cy="12.5" r="3.5" />
        <path d="M3 8c0-3.5 2.5-6 6.5-6 5 0 4.83 3 7.5 5s5 2 5 6c0 4.5-2.5 6.5-7 6.5-2.5 0-2.5 2.5-6 2.5s-7-2-7-5.5c0-3 1.5-3 1.5-5C3.5 10 3 9 3 8Z" />
      </svg>
    </button>
  </div>

  {#if mode === 'edit'}
    <button class="del" on:click|stopPropagation={() => dispatch('remove')} title="Remove">×</button>
  {/if}
</div>

<style>
  /* First 3 columns (name / gutter / pick) match SliderRow's/TriggerRow's own
     (name / lo-bound / track) pixel-for-pixel so this row's pick button lines
     up with every SliderRow's track above/below it in the same tab. The
     trailing column deliberately does NOT split 44px/68px like those two —
     see the icon-cluster comment above — but its combined width (112px) is
     identical, so the 1fr column ends up exactly as wide either way. */
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

  .pick {
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
  .pick:hover { color: var(--text); border-color: var(--border); }

  .icon-cluster {
    display: flex;
    align-items: center;
    /* flex-start, not flex-end — TriggerRow's own icon-cluster (3 icons, this
       row has 4) needs its first icon flush at the same x regardless of how
       many follow, or the two rows' leading icons drift apart by however much
       slack space right-alignment would leave in the shorter one. Trailing
       edge is allowed to differ between row types (fewer icons just leaves
       a bit of visible space before the row's edit-mode del button). */
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
    color: rgba(var(--text-rgb), 0.5);
    cursor: pointer;
    flex-shrink: 0;
    padding: 0;
  }
  .icon-btn:hover  { color: rgba(var(--text-rgb), 0.85); border-color: var(--border); }
  .icon-btn.active { background: rgba(var(--accent-rgb), 0.4); border-color: var(--accent); color: var(--text); }
  .icon-btn:disabled { opacity: 0.35; cursor: default; }
  .icon-btn:disabled:hover { color: rgba(var(--text-rgb), 0.5); border-color: var(--grid); }

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
