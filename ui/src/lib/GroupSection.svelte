<script>
  import { createEventDispatcher } from 'svelte'
  import SliderRow from './SliderRow.svelte'
  const dispatch = createEventDispatcher()

  export let group        = {}
  export let mode         = 'preview'
  export let selectedIds  = new Set()
  export let dropHighlight = false
  export let dropBefore    = false
  export let dropTarget    = null
  export let depth         = 0

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
      {#each group.sliders as slider (slider.id)}
        <SliderRow
          {slider} {mode}
          selected={selectedIds.has(slider.id)}
          dropAbove={dropTarget?.type === 'slider-row' && dropTarget.id === slider.id && dropTarget.pos === 'before'}
          dropBelow={dropTarget?.type === 'slider-row' && dropTarget.id === slider.id && dropTarget.pos === 'after'}
          on:change={e      => dispatch('sliderChange',      { id: slider.id, value: e.detail })}
          on:commit={e      => dispatch('sliderCommit',      { id: slider.id, value: e.detail })}
          on:select={e      => dispatch('sliderSelect',      { id: slider.id, multi: e.detail })}
          on:remove={()      => dispatch('sliderRemove',     { groupId: group.id, sliderId: slider.id })}
          on:dragStart={()  => dispatch('sliderDragStart',   { sliderId: slider.id, groupId: group.id })}
          on:dragEnd={()    => dispatch('sliderDragEnd')}
          on:rowDragOver={e => dispatch('sliderRowDragOver', { sliderId: slider.id, pos: e.detail })}
          on:rowDragLeave={()=> dispatch('sliderRowDragLeave',{ sliderId: slider.id })}
          on:rowDrop={e     => dispatch('sliderRowDrop',     { sliderId: slider.id, pos: e.detail })}
        />
      {/each}

      {#each group.groups ?? [] as subGroup (subGroup.id)}
        <svelte:self
          group={subGroup}
          {mode}
          {selectedIds}
          {dropTarget}
          depth={depth + 1}
          dropHighlight={dropTarget?.type === 'group-header' && dropTarget.id === subGroup.id && false}
          dropBefore={false}
          on:toggle
          on:rename
          on:remove
          on:sliderChange
          on:sliderCommit
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
    border-bottom: 1px solid #1c1c1c;
    position: relative;
  }

  .group.drop-before .group-header {
    background: #0d1a33 !important;
    outline: 1px solid #3b7fff55;
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
  /* depth-based header color */
  .group[style*="--depth:0"] .group-header { background: #161616; }
  .group[style*="--depth:1"] .group-header { background: #131313; }
  .group[style*="--depth:2"] .group-header { background: #111111; }

  .group-header:hover          { background: #1c1c1c !important; }
  .group-header.drop-highlight { background: #1a2a1a; outline: 1px solid #3a7a3a44; }

  .handle {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 20px;
    height: 100%;
    color: #333;
    cursor: grab;
    flex-shrink: 0;
  }
  .handle:hover  { color: #666; }
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
  .group[style*="--depth:0"] .chevron { color: #555; }
  .group[style*="--depth:1"] .chevron { color: #444; }
  .group[style*="--depth:2"] .chevron { color: #383838; }
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
  .group[style*="--depth:0"] .label { color: #777; }
  .group[style*="--depth:1"] .label { color: #5a5a5a; }
  .group[style*="--depth:2"] .label { color: #484848; }

  .label-input {
    flex: 1;
    background: #1e1e1e;
    border: 1px solid #3b7fff88;
    border-radius: 3px;
    color: #e2e2e2;
    font-size: 11px;
    font-family: inherit;
    padding: 1px 5px;
    outline: none;
  }

  .capture-btn {
    padding: 2px 6px;
    border: 1px solid #3b7fff33;
    border-radius: 3px;
    background: transparent;
    color: #6fa3ff66;
    font-size: 10px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
    flex-shrink: 0;
    white-space: nowrap;
  }
  .capture-btn:hover { border-color: #3b7fff88; color: #6fa3ffaa; }

  .del-group {
    width: 16px;
    height: 16px;
    border: none;
    background: transparent;
    color: #333;
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
  .del-group:hover { background: #2e2e2e; color: #888; }

  .group-body { }

  .empty-group {
    padding: 10px 28px;
    font-size: 11px;
    color: #333;
  }
</style>
