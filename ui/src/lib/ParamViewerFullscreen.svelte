<script>
  import { layout, allLeaves } from '../stores/layout.js'
  import { fullscreenTreeId } from '../stores/uiState.js'
  import DataTreeSunburst from './DataTreeSunburst.svelte'

  // Finds the control by id anywhere in the active workspace's layout
  // (top-level or nested in a group) — looked up live off the store so the
  // view stays correct if the row's data changes while fullscreen is open.
  function findControl(node, id) {
    for (const leaf of allLeaves(node)) {
      for (const tab of leaf.tabs) {
        const found = search(tab.sliders, tab.groups, id)
        if (found) return found
      }
    }
    return null
  }
  function search(sliders, groups, id) {
    const hit = sliders.find(s => s.id === id)
    if (hit) return hit
    for (const g of groups ?? []) {
      const found = search(g.sliders, g.groups, id)
      if (found) return found
    }
    return null
  }

  $: control = $fullscreenTreeId ? findControl($layout, $fullscreenTreeId) : null

  function close() {
    fullscreenTreeId.set(null)
  }

  let container
  let availW = 0, availH = 0
  $: size = Math.max(200, Math.min(availW, availH) - 48)
</script>

<svelte:window on:keydown={e => { if (e.key === 'Escape') close() }} />

{#if control}
  <div class="overlay">
    <div class="header">
      <span class="title">{control.name}</span>
      <button class="close" on:click={close} title="Close (Esc)">×</button>
    </div>
    <div class="body" bind:this={container} bind:clientWidth={availW} bind:clientHeight={availH}>
      {#if container}
        <DataTreeSunburst tree={control.tree} {size} id={control.id} showCounts={control.showCounts !== false} />
      {/if}
    </div>
  </div>
{/if}

<style>
  .overlay {
    position: fixed;
    inset: 0;
    z-index: 2000;
    background: var(--bg);
    display: flex;
    flex-direction: column;
  }

  .header {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 14px;
    border-bottom: 1px solid var(--grid);
  }
  .title {
    font-size: 13px;
    font-weight: 600;
    color: var(--text);
  }
  .close {
    width: 26px;
    height: 26px;
    border: none;
    background: transparent;
    color: rgba(var(--text-rgb), 0.5);
    font-size: 18px;
    line-height: 1;
    cursor: pointer;
    border-radius: 4px;
  }
  .close:hover { background: var(--grid); color: var(--text); }

  .body {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    min-height: 0;
  }
</style>
