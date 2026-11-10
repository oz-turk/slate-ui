<script>
  import { createEventDispatcher } from 'svelte'
  import { clickOutside } from './actions.js'
  const dispatch = createEventDispatcher()

  export let x     = 0
  export let y     = 0
  // [{ label, action, danger?, checked? }] — or the string 'sep' for a divider.
  // checked (true/false) makes the item an on/off setting: a checkbox (.cb, app.css) on the
  // right shows its state either way (a "✓ " prefix only shows "on", so "off"
  // read as nothing). Clicking one calls action(newValue), flips the box in
  // place and keeps the menu open, so it can be clicked again.
  export let items = []

  function click(item) {
    if (typeof item.checked === 'boolean') {
      item.checked = !item.checked
      items = items
      item.action(item.checked)
      return
    }
    item.action()
    dispatch('close')
  }
</script>

<div class="menu" use:clickOutside={{ onClose: () => dispatch('close') }} style="left: {x}px; top: {y}px">
  {#each items as item}
    {#if item === 'sep'}
      <div class="sep"></div>
    {:else}
      <button class="item" class:danger={item.danger} on:click={() => click(item)}>
        <span class="label">{item.label}</span>
        {#if typeof item.checked === 'boolean'}
          <span class="cb" class:checked={item.checked}></span>
        {/if}
      </button>
    {/if}
  {/each}
</div>

<style>
  .menu {
    position: fixed;
    min-width: 170px;
    background: var(--panel-bg);
    border: 1px solid var(--grid);
    border-radius: 6px;
    padding: 4px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
    z-index: 1000;
    max-height: calc(100vh - 16px);
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }

  .item {
    display: flex;
    align-items: center;
    gap: 12px;
    width: 100%;
    text-align: left;
    padding: 6px 10px;
    border: none;
    background: transparent;
    color: var(--text);
    font-size: 12px;
    font-family: inherit;
    cursor: pointer;
    border-radius: 4px;
    transition: background 0.1s;
  }
  .item:hover { background: rgba(var(--accent-rgb), 0.15); }
  .item.danger { color: rgba(var(--danger-rgb), 0.85); }
  .item.danger:hover { background: rgba(var(--danger-rgb), 0.15); }
  .label { flex: 1; }

  /* checkbox look: the shared .cb style in app.css */

  .sep {
    height: 1px;
    background: var(--grid);
    margin: 4px 2px;
    flex-shrink: 0;
  }
</style>
