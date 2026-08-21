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
  import { hoverHint } from '../stores/uiState.js'
  const dispatch = createEventDispatcher()

  // slider.type → row component (falls back to SliderRow when unset/unknown)
  const ROW_COMPONENTS = { toggle: ToggleRow, button: ButtonRow, valueList: ValueListRow, panel: PanelRow, itemPicker: ValueListRow, humanValueList: ValueListRow, colourPicker: ColourPickerRow, pancakeButton: ButtonRow }

  export let group        = {}
  export let mode         = 'preview'
  export let selectedIds  = new Set()
  export let dropHighlight = false
  export let dropBefore    = false
  export let dropTarget    = null
  export let depth         = 0
  export let resizingSliderId = null  // row currently being resized — flip is skipped for it

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

  function headerDragOver(e) { dispatch('headerDragOver') }
  function headerDragLeave(e) {
    if (!e.currentTarget.contains(e.relatedTarget)) dispatch('headerDragLeave')
  }
  function headerDrop(e) { dispatch('headerDrop') }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div class="group" class:drop-before={dropBefore} style="--depth:{depth}">
  <!-- svelte-ignore a11y-click-events-have-key-events -->
  <div class="group-header"
      class:drop-highlight={dropHighlight}
      style="padding-left: {6 + depth * 14}px"
      on:click={() => dispatch('toggle', group.id)}
      on:mouseenter={() => hoverHint.set('Groups keep sliders together — drag items onto the header to add them, drag the group itself to move or nest it')}
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
          on:dragstart={e => { e.dataTransfer.effectAllowed = 'move'; dispatch('headerDragStart') }}
          on:dragend={() => dispatch('groupDragEnd')}
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
      <!-- svelte-ignore a11y-autofocus -->
      <input class="label-input" bind:value={labelValue} autofocus
        on:blur={commitRename} on:keydown={onKeydown} on:click|stopPropagation />
    {:else}
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <span class="label" on:dblclick|stopPropagation={startRename}>{group.label ?? 'Group'}</span>
    {/if}

    {#if mode === 'edit'}
      <button class="capture-btn" on:click|stopPropagation={() => dispatch('capture', { groupId: group.id })} title="Capture selected sliders into group">
        + Capture
      </button>
      <button class="del-group" on:click|stopPropagation={() => dispatch('remove', group.id)} title="Delete group">×</button>
    {/if}
  </div>

  {#if !group.collapsed}
    <div class="group-body">
      {#if group.sliders.length === 0 && (group.groups ?? []).length === 0}
        <div class="empty-group">Empty group</div>
      {/if}

      {#each group.sliders as slider, i (slider.id)}
        <div class="row-slot" animate:flip={{ duration: slider.id === resizingSliderId ? 0 : 150, easing: cubicOut }}>
          <svelte:component
            this={ROW_COMPONENTS[slider.type] ?? SliderRow}
            {slider} {mode}
            selected={selectedIds.has(slider.id)}
            isFirst={i === 0}
            isLast={i === group.sliders.length - 1}
            on:change={e      => dispatch('sliderChange',      { id: slider.id, type: slider.type, value: e.detail, multiSelect: slider.multiSelect })}
            on:commit={e      => dispatch('sliderCommit',      { id: slider.id, type: slider.type, value: e.detail })}
            on:resize={e       => dispatch('sliderResize',       { id: slider.id, height: e.detail })}
            on:resizeCommit={e => dispatch('sliderResizeCommit', { id: slider.id, height: e.detail })}
            on:resizeStart={e  => dispatch('sliderResizeStart', e.detail)}
            on:resizeEnd={()   => dispatch('sliderResizeEnd')}
            on:select={e      => dispatch('sliderSelect',      { id: slider.id, multi: e.detail })}
            on:remove={()      => dispatch('sliderRemove',     { groupId: group.id, sliderId: slider.id })}
            on:dragStart={()  => dispatch('sliderDragStart',   { sliderId: slider.id, groupId: group.id })}
            on:dragEnd={()    => dispatch('sliderDragEnd')}
            on:rowDragOver={e => dispatch('sliderRowDragOver', { sliderId: slider.id, pos: e.detail, groupId: group.id })}
            on:rowDragLeave={()=> dispatch('sliderRowDragLeave',{ sliderId: slider.id })}
            on:rowDrop={e     => dispatch('sliderRowDrop',     { sliderId: slider.id, pos: e.detail })}
          />
        </div>
      {/each}

      {#each group.groups ?? [] as subGroup (subGroup.id)}
        <svelte:self
          group={subGroup}
          {mode}
          {selectedIds}
          {dropTarget}
          {resizingSliderId}
          depth={depth + 1}
          dropHighlight={dropTarget?.type === 'group-header' && dropTarget.id === subGroup.id && false}
          dropBefore={false}
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
      {/each}
    </div>
  {/if}
</div>

<style>
  .group {
    border-bottom: 1px solid var(--grid);
    position: relative;
  }

  .group.drop-before .group-header {
    background: rgba(var(--accent-rgb), 0.12) !important;
    outline: 1px solid rgba(var(--accent-rgb), 0.33);
  }

  .group-header {
    display: flex;
    align-items: center;
    gap: 4px;
    padding-right: 6px;
    height: 30px;
    cursor: pointer;
    user-select: none;
    transition: background 0.1s;
  }
  /* depth-based header color — each level a touch darker than --bg */
  .group[style*="--depth:0"] .group-header { background: var(--bg); }
  .group[style*="--depth:1"] .group-header { background: rgba(0, 0, 0, 0.15); }
  .group[style*="--depth:2"] .group-header { background: rgba(0, 0, 0, 0.28); }

  .group-header:hover          { background: var(--grid) !important; }
  .group-header.drop-highlight { background: #1a2a1a; outline: 1px solid #3a7a3a44; }

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

  .group-body { }
  .row-slot   { display: block; }

  .empty-group {
    padding: 10px 28px;
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.21);
  }
</style>
