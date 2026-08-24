<script>
  import { createEventDispatcher } from 'svelte'
  import { clickOutside } from './actions.js'
  const dispatch = createEventDispatcher()

  export let x     = 0
  export let y     = 0
  export let items = []   // [{ label, action, danger? }] — or the string 'sep' for a divider

  function click(item) {
    item.action()
    dispatch('close')
  }
</script>

<div class="menu" use:clickOutside={{ onClose: () => dispatch('close') }} style="left: {x}px; top: {y}px">
  {#each items as item}
    {#if item === 'sep'}
      <div class="sep"></div>
    {:else}
      <button class="item" class:danger={item.danger} on:click={() => click(item)}>{item.label}</button>
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
    display: flex;
    flex-direction: column;
    gap: 1px;
  }

  .item {
    display: flex;
    align-items: center;
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

  .sep {
    height: 1px;
    background: var(--grid);
    margin: 4px 2px;
    flex-shrink: 0;
  }
</style>
