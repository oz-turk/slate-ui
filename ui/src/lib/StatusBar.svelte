<script>
  import { onMount, afterUpdate } from 'svelte'
  import { mode, hoverHint, altHeld, ctrlHeld, docStatus, saveFlash } from '../stores/uiState.js'

  let hintEl
  let overflowing = false

  function checkOverflow() {
    if (hintEl) overflowing = hintEl.scrollWidth > hintEl.clientWidth + 1
  }

  // "Saved 5 min ago" is relative, so it re-renders on a clock tick.
  let now = Date.now()
  onMount(() => {
    checkOverflow()
    window.addEventListener('resize', checkOverflow)
    const clock = setInterval(() => now = Date.now(), 15000)
    return () => { window.removeEventListener('resize', checkOverflow); clearInterval(clock) }
  })
  // Fresh "now" whenever a new status arrives (a save just happened).
  $: $docStatus, now = Date.now()

  function fmtSolve(ms) {
    if (ms == null) return null
    return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(ms < 10000 ? 2 : 1)} s`
  }
  function fmtSaved(at, now) {
    if (at == null) return null
    const s = Math.max(0, (now - at) / 1000)
    if (s < 60)    return 'just now'
    if (s < 3600)  return `${Math.floor(s / 60)} min ago`
    if (s < 86400) return `${Math.floor(s / 3600)} h ago`
    const d = new Date(at)
    return `${d.toLocaleDateString(undefined, { day: 'numeric', month: 'short' })} ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}`
  }
  $: solveText = fmtSolve($docStatus.solveMs)
  $: savedText = fmtSaved($docStatus.savedAt, now)
  $: savedTitle = $docStatus.savedAt ? `Last saved ${new Date($docStatus.savedAt).toLocaleString()}` : ''

  afterUpdate(checkOverflow)
</script>

<div class="status-bar">
  <div class="hint" class:overflowing bind:this={hintEl}>
    {#if $altHeld}
      <span class="hint-item">
        <span class="kbd">Alt</span> + <span class="kbd">Drag divider</span>
        Break off from group
      </span>
      {#if $mode === 'edit'}
        <span class="hint-item">
          <span class="kbd">Alt</span> + <span class="kbd">Drag corner</span>
          Split spanning window
        </span>
      {/if}
    {:else if $ctrlHeld}
      {#if $hoverHint}
        <span class="hint-item">{$hoverHint}</span>
      {:else}
        <span class="hint-item">
          <span class="kbd">Ctrl</span> +
          <span class="kbd">
            <!-- mouse-wheel: composite icon, body shape based on Lucide "mouse" (ISC) with a filled scroll-wheel highlight — see NOTICE.md -->
            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect x="5" y="2" width="14" height="20" rx="7" />
              <rect x="10" y="6" width="4" height="6" rx="2" fill="currentColor" stroke="none" />
            </svg>
          </span>
          Zoom
        </span>
      {/if}
      {#if $mode === 'edit'}
        <span class="hint-item">
          <span class="kbd">Ctrl</span> + click
          Multi-select
        </span>
        <span class="hint-item">
          <span class="kbd">Shift</span> + click
          Range select
        </span>
      {/if}
      <span class="hint-item">
        <span class="kbd">Ctrl</span> + <span class="kbd">Z</span>
        Undo
      </span>
      <span class="hint-item">
        <span class="kbd">Ctrl</span> + 1-9
        Switch workspace
      </span>
    {:else if $hoverHint}
      <span class="hint-item">{$hoverHint}</span>
    {:else if $mode === 'edit'}
      <span class="hint-item">
        <span class="kbd">
          <!-- mouse-left: composite icon, body shape based on Lucide "mouse" (ISC) with a filled left-button highlight — see NOTICE.md -->
          <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="5" y="2" width="14" height="20" rx="7" />
            <line x1="12" y1="2" x2="12" y2="8" />
            <line x1="5" y1="8" x2="19" y2="8" />
            <rect x="6" y="3" width="6" height="5" rx="2" fill="currentColor" stroke="none" />
          </svg>
        </span>
        Select
      </span>
      <span class="hint-item">
        <span class="kbd">Shift</span> +
        <span class="kbd">
          <!-- mouse-left: composite icon, body shape based on Lucide "mouse" (ISC) with a filled left-button highlight — see NOTICE.md -->
          <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="5" y="2" width="14" height="20" rx="7" />
            <line x1="12" y1="2" x2="12" y2="8" />
            <line x1="5" y1="8" x2="19" y2="8" />
            <rect x="6" y="3" width="6" height="5" rx="2" fill="currentColor" stroke="none" />
          </svg>
        </span>
        Range select
      </span>
      <span class="hint-item">
        <span class="kbd">Drag</span>
        Move
      </span>
      <span class="hint-item">
        <span class="kbd">x</span>
        Delete
      </span>
      <span class="hint-item">
        <span class="kbd">c</span>
        Capture
      </span>
      <span class="hint-item">
        <span class="kbd">g</span>
        Group selected
      </span>
      <span class="hint-item">
        <span class="kbd">Esc</span>
        Clear selection
      </span>
      <span class="hint-item">
        <span class="kbd">Ctrl</span> + <span class="kbd">Z</span>
        Undo
      </span>
    {:else}
      <span class="hint-item">
        <span class="kbd">Tab</span>
        Edit
      </span>
    {/if}
  </div>
  <div class="doc-status">
    {#if solveText}
      <span class="stat" title="Duration of the last Grasshopper solution">Solve {solveText}</span>
    {/if}
    {#if $saveFlash === 'ok'}
      <span class="stat flash ok">✓ Saved</span>
    {:else if $saveFlash === 'failed'}
      <span class="stat flash failed">Save cancelled</span>
    {:else if savedText}
      <span class="stat" title={savedTitle}>
        {#if $docStatus.modified}<span class="dirty" title="Unsaved changes"></span>{/if}
        Saved {savedText}
      </span>
    {:else if $docStatus.modified}
      <span class="stat"><span class="dirty"></span>Not saved yet</span>
    {/if}
    <span class="version">v{__SLATE_VERSION__} ({__SLATE_COMMIT__})</span>
  </div>
</div>

<style>
  .status-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    height: 22px;
    padding: 0 10px;
    flex-shrink: 0;
    background: var(--panel-bg);
    border-top: 1px solid var(--bg);
  }

  .hint {
    display: flex;
    align-items: center;
    gap: 16px;
    overflow-x: auto;
    scrollbar-width: none;
  }
  .hint::-webkit-scrollbar { display: none; }

  .hint.overflowing {
    mask-image: linear-gradient(to right, black calc(100% - 12px), transparent);
    -webkit-mask-image: linear-gradient(to right, black calc(100% - 12px), transparent);
  }

  .hint-item {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.55);
    white-space: nowrap;
    flex-shrink: 0;
  }

  .kbd {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    padding: 1px 5px;
    border: 1px solid rgba(var(--text-rgb), 0.25);
    border-radius: 4px;
    color: var(--text);
    font-size: 9px;
    font-weight: 600;
    line-height: 1.5;
  }

  .version {
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.29);
    flex-shrink: 0;
    font-variant-numeric: tabular-nums;
  }

  .doc-status {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-shrink: 0;
  }
  .stat {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.45);
    white-space: nowrap;
    font-variant-numeric: tabular-nums;
  }
  /* Unsaved changes since the last save */
  .dirty {
    width: 5px;
    height: 5px;
    border-radius: 50%;
    background: var(--accent-light);
  }
  .flash { font-weight: 600; animation: save-flash 2.5s ease-out; }
  .flash.ok     { color: var(--accent); }
  .flash.failed { color: rgba(var(--danger-rgb), 0.85); }
  @keyframes save-flash {
    0%   { opacity: 0; transform: translateY(3px); }
    10%  { opacity: 1; transform: none; }
    80%  { opacity: 1; }
    100% { opacity: 0.6; }
  }
</style>
