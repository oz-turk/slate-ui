<script>
  import { createEventDispatcher, onMount } from 'svelte'
  const dispatch = createEventDispatcher()

  export let x     = 0
  export let y     = 0
  export let items = []   // [{ label, action, danger? }] — or the string 'sep' for a divider

  let menuEl

  function onDocPointerDown(e) {
    if (menuEl && !menuEl.contains(e.target)) dispatch('close')
  }
  function onKeydown(e) {
    if (e.key === 'Escape') dispatch('close')
  }

  onMount(() => {
    // capture phase so this fires before the click that opened us (context menus
    // are opened by a native `contextmenu` event, not a click, so no self-close race)
    window.addEventListener('pointerdown', onDocPointerDown, true)
    window.addEventListener('keydown', onKeydown)
    return () => {
      window.removeEventListener('pointerdown', onDocPointerDown, true)
      window.removeEventListener('keydown', onKeydown)
    }
  })

  function click(item) {
    item.action()
    dispatch('close')
  }
</script>

<div class="menu" bind:this={menuEl} style="left: {x}px; top: {y}px">
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
