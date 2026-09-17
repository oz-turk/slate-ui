<script>
  import { createEventDispatcher } from 'svelte'
  import { flip } from 'svelte/animate'
  import { cubicOut } from 'svelte/easing'
  import SliderRow from './SliderRow.svelte'
  import ToggleRow from './ToggleRow.svelte'
  import ButtonRow from './ButtonRow.svelte'
  import ValueListRow from './ValueListRow.svelte'
  import PanelRow from './PanelRow.svelte'
  import ColourPickerRow from './ColourPickerRow.svelte'
  import TriggerRow from './TriggerRow.svelte'
  import GeometryParamRow from './GeometryParamRow.svelte'
  import { hoverHint } from '../stores/uiState.js'
  import { orderedItems } from '../stores/layout.js'
  const dispatch = createEventDispatcher()

  // slider.type → row component (falls back to SliderRow when unset/unknown)
  const ROW_COMPONENTS = { toggle: ToggleRow, button: ButtonRow, valueList: ValueListRow, panel: PanelRow, itemPicker: ValueListRow, humanValueList: ValueListRow, colourPicker: ColourPickerRow, pancakeButton: ButtonRow, trigger: TriggerRow, geometryParam: GeometryParamRow }

  export let group        = {}
  export let mode         = 'preview'
  export let selectedIds  = new Set()
  export let dropHighlight = false
  export let dropNest      = false
  export let dropBeforeMe  = false
  export let dropAfterMe   = false
  export let dropTarget    = null
  export let activeDrag    = null
  export let depth         = 0
  // id of the row currently being resized, or null — passed down from Pane.
  // While set, flip is skipped for the whole list (see row-slot below), not
  // just this row: see Pane.svelte's resizingSliderId comment for why.
  export let resizingSliderId = null
  // The id of whichever container (a parent group, or null for top-level)
  // this group instance itself lives in — rides along on headerDragOver/Drop
  // so Pane.svelte always knows exactly where to insert a dragged group,
  // same as sliderRowDragOver already carries groupId for slider rows.
  export let containerId   = null

  $: items = orderedItems(group)

  let editingLabel = false
  let labelValue   = ''

  function startRename() {
    if (mode !== 'edit') return
    editingLabel = true
    labelValue   = group.label ?? ''
  }

  function commitRename() {
    editingLabel = false
    if (labelValue.trim()) dispatch('rename', { id: group.id, label: labelValue.trim() })
  }

  function onKeydown(e) {
    if (e.key === 'Enter')  { commitRename(); e.preventDefault() }
    if (e.key === 'Escape') { editingLabel = false }
  }
  function focusAndSelect(node) {
    node.focus()
    node.select()
  }

  // Header is split into three drop zones so a dragged group can be reordered
  // as a sibling (top/bottom edge, like slider rows) as well as nested inside
  // (middle band) — top-level VS Code/Notion-style tree convention.
  function headerZone(e) {
    const rect = e.currentTarget.getBoundingClientRect()
    const frac = (e.clientY - rect.top) / rect.height
    return frac < 0.25 ? 'before' : frac > 0.75 ? 'after' : 'nest'
  }
  // Every dispatch below carries group.id (and containerId, the container
  // THIS group lives in) explicitly rather than relying on the listener's
  // own closure — these events bubble up unchanged through a chain of bare
  // `on:headerDragOver` forwards on nested <svelte:self>, so by the time
  // Pane.svelte's listener sees one, it has no way to tell which depth (or
  // which parent container) it originated at unless the payload says so
  // (same reason sliderId+groupId ride along in sliderRowDragOver's payload —
  // this lets a dragged group target a slider row's container directly
  // instead of needing to search the whole tree for it).
  function headerDragOver(e) { dispatch('headerDragOver', { id: group.id, pos: headerZone(e), groupId: containerId }) }
  function headerDragLeave(e) {
    if (!e.currentTarget.contains(e.relatedTarget)) dispatch('headerDragLeave', group.id)
  }
  function headerDrop(e) { dispatch('headerDrop', { id: group.id, pos: headerZone(e), groupId: containerId }) }

  // Same "fade the thing being dragged" treatment SliderRow gives its own
  // .row via row-dragging — applied to the outer .group div so it dims the
  // header AND everything inside (child sliders, nested subgroups) as one
  // unit, for free, via plain CSS opacity inheritance.
  let groupDragging = false
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div class="group" class:nested={depth > 0} class:drop-before-me={dropBeforeMe} class:drop-after-me={dropAfterMe} class:group-dragging={groupDragging} data-group-id={group.id} style="--depth:{depth}">
  <!-- svelte-ignore a11y-click-events-have-key-events -->
  <div class="group-header"
      class:drop-highlight={dropHighlight}
      class:drop-nest={dropNest}
      on:click={() => dispatch('toggle', group.id)}
      on:mouseenter={() => hoverHint.set('Double-click: rename  ·  Drag items onto the header to add them, drag the group itself to reorder (top/bottom edge) or nest it (middle)')}
      on:mouseleave={() => hoverHint.set(null)}
      on:dragover|preventDefault={headerDragOver}
      on:dragleave={headerDragLeave}
      on:drop|preventDefault={headerDrop}
  >
    {#if mode === 'edit'}
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <div class="handle"
          draggable="true"
          on:click|stopPropagation
          on:dragstart={e => { e.dataTransfer.effectAllowed = 'move'; groupDragging = true; dispatch('headerDragStart', group.id) }}
          on:dragend={() => { groupDragging = false; dispatch('groupDragEnd') }}
      >
        <svg width="8" height="12" viewBox="0 0 8 12" fill="currentColor">
          <circle cx="2" cy="2"  r="1.2"/><circle cx="6" cy="2"  r="1.2"/>
          <circle cx="2" cy="6"  r="1.2"/><circle cx="6" cy="6"  r="1.2"/>
          <circle cx="2" cy="10" r="1.2"/><circle cx="6" cy="10" r="1.2"/>
        </svg>
      </div>
    {/if}

    <span class="chevron" class:open={!group.collapsed}>›</span>

    {#if editingLabel}
      <input class="label-input" bind:value={labelValue} use:focusAndSelect
        on:blur={commitRename} on:keydown={onKeydown} on:click|stopPropagation />
    {:else}
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <span class="label" on:click|stopPropagation on:dblclick|stopPropagation={startRename}>{group.label ?? 'Group'}</span>
    {/if}

    {#if mode === 'edit'}
      <button class="capture-btn" on:click|stopPropagation={() => dispatch('capture', { groupId: group.id })} title="Capture selected sliders into group">
        + Capture
      </button>
      <button class="del-group" on:click|stopPropagation={() => dispatch('remove', group.id)} title="Delete group">×</button>
    {/if}
  </div>

  {#if !group.collapsed}
    <div class="group-body" style="margin-left: {mode === 'edit' ? 36 : 12}px">
      {#if group.sliders.length === 0 && (group.groups ?? []).length === 0}
        <div class="empty-group">Empty group</div>
      {/if}

      {#each items as item, i (item.id)}
        <div class="row-slot" animate:flip={{ duration: resizingSliderId ? 0 : 150, easing: cubicOut }}>
          {#if item.kind === 'slider'}
            {@const slider = item.data}
            <svelte:component
              this={ROW_COMPONENTS[slider.type] ?? SliderRow}
              {slider} {mode}
              selected={selectedIds.has(slider.id)}
              isFirst={i === 0}
              isLast={i === items.length - 1}
              on:change={e      => dispatch('sliderChange',      { id: slider.id, type: slider.type, value: e.detail, multiSelect: slider.multiSelect })}
              on:commit={e      => dispatch('sliderCommit',      { id: slider.id, type: slider.type, value: e.detail })}
              on:resize={e       => dispatch('sliderResize',       { id: slider.id, height: e.detail })}
              on:resizeCommit={e => dispatch('sliderResizeCommit', { id: slider.id, height: e.detail })}
              on:resizeStart={e  => dispatch('sliderResizeStart', e.detail)}
              on:resizeEnd={()   => dispatch('sliderResizeEnd')}
              on:select={e      => dispatch('sliderSelect',      { id: slider.id, shift: e.detail.shift, ctrl: e.detail.ctrl })}
              on:remove={()      => dispatch('sliderRemove',     { groupId: group.id, sliderId: slider.id })}
              on:dragStart={()  => dispatch('sliderDragStart',   { sliderId: slider.id, groupId: group.id })}
              on:dragEnd={()    => dispatch('sliderDragEnd')}
              on:rowDragOver={e => dispatch('sliderRowDragOver', { sliderId: slider.id, pos: e.detail, groupId: group.id })}
              on:rowDragLeave={()=> dispatch('sliderRowDragLeave',{ sliderId: slider.id })}
              on:rowDrop={e     => dispatch('sliderRowDrop',     { sliderId: slider.id, pos: e.detail })}
            />
          {:else}
            {@const subGroup = item.data}
            <svelte:self
              group={subGroup}
              {mode}
              {selectedIds}
              {dropTarget}
              {resizingSliderId}
              {activeDrag}
              depth={depth + 1}
              containerId={group.id}
              dropHighlight={dropTarget?.type === 'group-header' && dropTarget.id === subGroup.id && activeDrag?.type === 'slider'}
              dropNest={dropTarget?.type === 'group-header' && dropTarget.id === subGroup.id && dropTarget.pos === 'nest' && activeDrag?.type === 'group'}
              dropBeforeMe={dropTarget?.type === 'group-header' && dropTarget.id === subGroup.id && dropTarget.pos === 'before' && activeDrag?.type === 'group'}
              dropAfterMe={dropTarget?.type === 'group-header' && dropTarget.id === subGroup.id && dropTarget.pos === 'after' && activeDrag?.type === 'group'}
              on:toggle
              on:rename
              on:remove
              on:sliderChange
              on:sliderCommit
              on:sliderResize
              on:sliderResizeCommit
              on:sliderResizeStart
              on:sliderResizeEnd
              on:sliderSelect
              on:sliderRemove
              on:headerDragStart
              on:headerDragOver
              on:headerDragLeave
              on:headerDrop
              on:groupDragEnd
              on:sliderDragStart
              on:sliderDragEnd
              on:sliderRowDragOver
              on:sliderRowDragLeave
              on:sliderRowDrop
              on:capture
            />
          {/if}
        </div>
      {/each}
    </div>
  {/if}
</div>

<style>
  .group {
    position: relative;
  }
  /* A nested group sits inside its parent's .group-body, which already pushes
     children right by its own border-left (1px) + padding-left (8px) — on top
     of this group's own margin-left below (set inline, which positions ITS
     line under ITS OWN chevron the same way at every depth), that compounds
     into a bigger gap between a parent's line and a child's line than between
     the pane edge and a top-level group's line (which has no such parent step
     to compound with). Cancelling that inherited step here — instead of
     shrinking the inline margin-left itself, which would pull the line out
     from under this group's own chevron — keeps every line-to-line gap equal
     to the pane-to-first-line one, and the chevron alignment intact at every
     depth (width:auto absorbs the 9px back on the right, so nothing overflows). */
  .group.nested {
    margin-left: -9px;
  }
  .group-header.drop-nest {
    background: rgba(var(--accent-rgb), 0.12) !important;
    outline: 1px solid rgba(var(--accent-rgb), 0.33);
  }

  /* Top/bottom edge of the header = reorder as a sibling, not nest — shown as
     an insertion line (same idea as sliders visibly shifting on live reorder,
     but a group block is tall enough that the shift alone isn't always obvious). */
  .group.drop-before-me::before,
  .group.drop-after-me::after {
    content: '';
    position: absolute;
    left: 0; right: 0;
    height: 2px;
    background: var(--accent);
    z-index: 1;
  }
  .group.drop-before-me::before { top: -1px; }
  .group.drop-after-me::after   { bottom: -1px; }

  /* Mirrors SliderRow's .row.row-dragging — dims the group being dragged
     (header + everything inside it) so it reads clearly as "this is what's
     moving" against the live-reordering siblings around it. */
  .group.group-dragging { opacity: 0.5; z-index: 2; }

  .group-header {
    display: flex;
    align-items: center;
    gap: 4px;
    padding-left: 6px;
    padding-right: 6px;
    height: 44px;
    cursor: pointer;
    user-select: none;
    transition: background 0.1s;
  }
  /* depth-based header color — each level a touch darker than --bg */
  .group[style*="--depth:0"] .group-header { background: var(--bg); }
  .group[style*="--depth:1"] .group-header { background: rgba(0, 0, 0, 0.15); }
  .group[style*="--depth:2"] .group-header { background: rgba(0, 0, 0, 0.28); }

  .group-header:hover          { background: var(--grid) !important; }
  .group-header.drop-highlight { background: rgba(var(--accent-rgb), 0.25); outline: 1px solid rgba(var(--accent-rgb), 0.6); }

  .handle {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 20px;
    height: 100%;
    color: rgba(var(--text-rgb), 0.2);
    cursor: grab;
    flex-shrink: 0;
  }
  .handle:hover  { color: rgba(var(--text-rgb), 0.43); }
  .handle:active { cursor: grabbing; }

  .chevron {
    font-size: 14px;
    transition: transform 0.15s;
    display: inline-block;
    transform: rotate(0deg);
    width: 12px;
    flex-shrink: 0;
  }
  /* depth-based chevron/label color */
  .group[style*="--depth:0"] .chevron { color: rgba(var(--text-rgb), 0.36); }
  .group[style*="--depth:1"] .chevron { color: rgba(var(--text-rgb), 0.29); }
  .group[style*="--depth:2"] .chevron { color: rgba(var(--text-rgb), 0.22); }
  .chevron.open { transform: rotate(90deg); }

  .label {
    flex: 1;
    font-size: 11px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .group[style*="--depth:0"] .label { color: rgba(var(--text-rgb), 0.47); }
  .group[style*="--depth:1"] .label { color: rgba(var(--text-rgb), 0.35); }
  .group[style*="--depth:2"] .label { color: rgba(var(--text-rgb), 0.28); }

  .label-input {
    flex: 1;
    background: var(--panel-bg);
    border: 1px solid rgba(var(--accent-rgb), 0.53);
    border-radius: 3px;
    color: var(--text);
    font-size: 11px;
    font-family: inherit;
    padding: 1px 5px;
    outline: none;
  }

  .capture-btn {
    padding: 2px 6px;
    border: 1px solid rgba(var(--accent-rgb), 0.2);
    border-radius: 3px;
    background: transparent;
    color: var(--accent-light);
    font-size: 10px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
    flex-shrink: 0;
    white-space: nowrap;
  }
  .capture-btn:hover { border-color: rgba(var(--accent-rgb), 0.53); color: var(--accent-light); }

  .del-group {
    width: 16px;
    height: 16px;
    border: none;
    background: transparent;
    color: rgba(var(--text-rgb), 0.21);
    font-size: 13px;
    cursor: pointer;
    border-radius: 3px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    transition: background 0.1s, color 0.1s;
    flex-shrink: 0;
  }
  .del-group:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.58); }

  /* Tree-indent guide: every nesting level shifts ALL of its content right
     by the same step, leaf rows included — not just group headers — so a
     slider two groups deep visually lines up under its own group instead of
     starting flush with top-level items. Compounds naturally with nesting
     since each subgroup's own .group-body applies the same padding again.
     margin-left (set inline, above) lines the border up under the chevron's
     own centre — 12px in preview, 36px in edit where the drag handle pushes
     the chevron over by 24px (20px handle + 4px flex gap). */
  .group-body {
    padding-left: 8px;
    border-left: 1px solid rgba(var(--text-rgb), 0.1);
  }
  .row-slot   { display: block; }

  .empty-group {
    padding: 10px 28px;
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.21);
  }
</style>
