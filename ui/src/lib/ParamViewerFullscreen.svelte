<script>
  import { layout, allLeaves, syncControl } from '../stores/layout.js'
  import { fullscreenTreeId, mode } from '../stores/uiState.js'
  import { postStateSnapshot } from './ipc.js'
  import DataTreeSunburst from './DataTreeSunburst.svelte'
  import PathMapperPanel from './PathMapperPanel.svelte'
  import { commitReorder } from './mapperActions.js'
  import GraphControls from './GraphControls.svelte'

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

  $: isMapper = control?.source === 'mapper'
  // Same persisted-field patch the row uses (syncControl fans out by sourceId,
  // pane-agnostic — see onShowCountsChange below).
  function onPatch(e) { syncControl(control.sourceId ?? control.id, e.detail); postStateSnapshot() }
  function onReorder(e) { onPatch({ detail: { mapperOrders: commitReorder(control, e.detail) } }) }

  function close() {
    fullscreenTreeId.set(null)
  }

  let container
  let availW = 0, availH = 0
  $: size = Math.max(200, Math.min(availW, availH) - 48)

  $: showCounts = control?.showCounts !== false
  // syncControl matches by sourceId when the slider has one (see layout.js's
  // matchesSource — it's how a push fans a change out to every Alt-drag copy
  // at once), so passing the bare id silently matched nothing for any
  // captured/copied control and the toggle looked dead (2026-09-27).
  function onShowCountsChange(e) { syncControl(control.sourceId ?? control.id, { showCounts: e.detail }); postStateSnapshot() }
</script>

<svelte:window on:keydown={e => { if (e.key === 'Escape') close() }} />

{#if control}
  <div class="overlay" data-vp-overlay>
    <div class="header">
      <span class="title">{control.name}</span>
      <div class="actions">
        <button class="close" on:click={close} title="Close (Esc)">×</button>
      </div>
    </div>
    {#if isMapper}
      <div class="mapper-strip"><PathMapperPanel slider={control} mode={$mode} on:patch={onPatch} /></div>
    {/if}
    <div class="body" bind:this={container} bind:clientWidth={availW} bind:clientHeight={availH}>
      {#if $mode === 'edit'}
        <GraphControls {showCounts} sizeLocked on:showCountsChange={onShowCountsChange} />
      {/if}
      {#if container}
        <DataTreeSunburst tree={control.tree} {size} id={control.sourceId ?? control.id} {showCounts}
          reorderable={isMapper && control.mapperManual !== true} on:reorder={onReorder} />
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

  .actions {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  /* Same box as ParamViewerRow's settings/expand/remove cluster — 2026-09-27
     kullanıcı: "iconların arka plan renkleri ve boyutları da tutarlı olsun". */
  .close {
    width: 24px;
    height: 24px;
    display: flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 4px;
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.5);
    font-size: 18px;
    line-height: 1;
    cursor: pointer;
    padding: 0;
    transition: background 0.1s, color 0.15s;
  }
  .close:hover { background: var(--border); color: rgba(var(--text-rgb), 0.85); }

  /* Rules strip above the graph — capped so a long rule list scrolls
     instead of squeezing the graph out. */
  .mapper-strip {
    flex-shrink: 0;
    max-height: 38%;
    overflow-y: auto;
    border-bottom: 1px solid var(--grid);
    padding: 4px 0;
  }

  .body {
    position: relative;
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    min-height: 0;
  }
</style>
