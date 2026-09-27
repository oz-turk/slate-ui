<script>
  import { layout, allLeaves, syncControl } from '../stores/layout.js'
  import { fullscreenTreeId, mode } from '../stores/uiState.js'
  import { postStateSnapshot } from './ipc.js'
  import DataTreeSunburst from './DataTreeSunburst.svelte'
  import ParamViewerSettingsPopup from './ParamViewerSettingsPopup.svelte'

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

  // Graph settings, reachable from fullscreen too now (2026-09-27 kullanıcı:
  // "full screen'deyken edit moda giremiyorum" — turned out to mean there was
  // no way to reach ParamViewerRow's settings popup from here at all). Gated
  // by the same global edit mode as ParamViewerRow's own corner-btn
  // (2026-09-27 kullanıcı: "sadece edit modunda 3 nokta görünmeli") —
  // fullscreen has no paneId/mode prop of its own, so it reads the app-wide
  // store directly. Size stepper is locked here (2026-09-27 kullanıcı: "tam
  // boyutu kilitli olacak") — fullscreen's own display size always comes
  // from the window (the `size` reactive block above), not from
  // slider.paramViewerSize, so letting it be edited here wouldn't do
  // anything visible. The popup shows the row's own stored preview size
  // (always a clean MODULE multiple) rather than the window-derived `size`,
  // so its "N×" readout never shows a fraction. Only the N-count toggle
  // stays live, via syncControl, which is pane-agnostic (unlike
  // Pane.svelte's own patchSlider, scoped to a paneId this global overlay
  // doesn't have).
  $: sunburstPreviewSize = control?.paramViewerSize ?? 352
  $: showCounts = control?.showCounts !== false
  let settingsPopup = null   // { x, y } | null
  $: if ($mode !== 'edit') settingsPopup = null
  function openSettings(e) {
    const rect = e.currentTarget.getBoundingClientRect()
    const w = 176, h = 140
    settingsPopup = {
      x: Math.min(rect.right - w, window.innerWidth  - w - 8),
      y: Math.min(rect.bottom + 4, window.innerHeight - h - 8),
    }
  }
  // syncControl matches by sourceId when the slider has one (see layout.js's
  // matchesSource — it's how a push fans a change out to every Alt-drag copy
  // at once), so passing the bare id silently matched nothing for any
  // captured/copied control and the toggle looked dead (2026-09-27).
  function onShowCountsChange(e) { syncControl(control.sourceId ?? control.id, { showCounts: e.detail }); postStateSnapshot() }
</script>

<svelte:window on:keydown={e => { if (e.key === 'Escape') close() }} />

{#if control}
  <div class="overlay">
    <div class="header">
      <span class="title">{control.name}</span>
      <div class="actions">
        {#if $mode === 'edit'}
          <button class="corner-btn" on:click={openSettings} title="Graph settings">
            <svg width="3" height="12" viewBox="0 0 3 12" fill="currentColor">
              <circle cx="1.5" cy="1.5" r="1.5"/><circle cx="1.5" cy="6" r="1.5"/><circle cx="1.5" cy="10.5" r="1.5"/>
            </svg>
          </button>
        {/if}
        <button class="close" on:click={close} title="Close (Esc)">×</button>
      </div>
    </div>
    <div class="body" bind:this={container} bind:clientWidth={availW} bind:clientHeight={availH}>
      {#if container}
        <DataTreeSunburst tree={control.tree} {size} id={control.sourceId ?? control.id} {showCounts} />
      {/if}
    </div>
    {#if settingsPopup}
      <ParamViewerSettingsPopup x={settingsPopup.x} y={settingsPopup.y}
        size={sunburstPreviewSize} sizeLocked {showCounts}
        on:showCountsChange={onShowCountsChange}
        on:close={() => settingsPopup = null} />
    {/if}
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
  .corner-btn, .close {
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
  .corner-btn:hover, .close:hover { background: var(--border); color: rgba(var(--text-rgb), 0.85); }

  .body {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    min-height: 0;
  }
</style>
