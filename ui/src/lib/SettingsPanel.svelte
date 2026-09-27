<script>
  import { settingsOpen, theme, allowMultipleCaptures } from '../stores/uiState.js'
  import { undoLimit } from '../stores/history.js'
  import { postStateSnapshot } from './ipc.js'
  import { clickOutside } from './actions.js'

  // `critical` marks the ~10 shown by default — the rest sit behind "Show
  // more" so the list reads as a quick reference instead of a wall of text
  // (2026-09-27 kullanıcı: "sadece kritik öneme sahip olan 10 tane filan
  // gösterip diğerlerini show more altında gizleyebiliriz").
  const shortcuts = [
    { keys: ['Tab'],          desc: 'Toggle Edit / Preview', critical: true },
    { keys: ['Click'],        desc: 'Select a control (edit mode)', critical: true },
    { keys: ['Ctrl', 'Click'],  desc: 'Add to selection' },
    { keys: ['Shift', 'Click'], desc: 'Select range from last click' },
    { keys: ['Esc'],          desc: 'Clear selection' },
    { keys: ['Drag'],         desc: 'Reorder / move controls', critical: true },
    { keys: ['Alt', 'drag'],  desc: 'Copy a control instead of moving it', critical: true },
    { keys: ['x'],            desc: 'Delete control / ungroup group under mouse', critical: true },
    { keys: ['Alt', 'x'],     desc: 'Delete every copy of the control under mouse', critical: true },
    { keys: ['c'],            desc: 'Capture into pane under mouse', critical: true },
    { keys: ['g'],            desc: 'Group selected controls', critical: true },
    { keys: ['Right-click'],  desc: 'Pane menu — split / close', critical: true },
    { keys: ['Corner drag'],  desc: 'Split pane, or drag out to collapse a neighbour' },
    { keys: ['Ctrl', '1–9'],  desc: 'Switch workspace' },
    { keys: ['Ctrl', 'Z'],    desc: 'Undo', critical: true },
    { keys: ['Ctrl', 'Scroll'], desc: 'Zoom the UI (WebView2 default)' },
    { keys: ['Ctrl', 'drag'], desc: 'Snap resize to slider-row size' },
    { keys: ['Alt', 'window corner drag'], desc: 'Add a pane at the edge without disturbing the rest of the layout' },
    { keys: ['Edge drag'],          desc: 'Aligned edges move together automatically' },
    { keys: ['Alt', 'edge drag'],   desc: 'Break this edge off the group (or just shake it)' },
  ]
  let showAllShortcuts = false
  $: visibleShortcuts = showAllShortcuts ? shortcuts : shortcuts.filter(s => s.critical)

  function clampUndoLimit(e) {
    const n = Math.max(1, Math.min(50, parseInt(e.target.value, 10) || 1))
    undoLimit.set(n)
  }

  function setTheme(t) {
    theme.set(t)
    try { localStorage.setItem('slate-theme', t) } catch {}
    postStateSnapshot()
  }
</script>

<div class="panel" use:clickOutside={{ onClose: () => settingsOpen.set(false), exclude: '[data-settings-toggle]' }}>
  <div class="section-title">Shortcuts</div>
  <div class="shortcuts" class:fade={!showAllShortcuts}>
    {#each visibleShortcuts as s}
      <div class="row">
        <span class="keys">
          {#each s.keys as k, i}
            {#if i > 0}<span class="plus">+</span>{/if}
            <span class="kbd">{k}</span>
          {/each}
        </span>
        <span class="desc">{s.desc}</span>
      </div>
    {/each}
  </div>
  <button class="show-more" on:click={() => showAllShortcuts = !showAllShortcuts}>
    <svg class:open={showAllShortcuts} width="9" height="9" viewBox="0 0 9 9" fill="none">
      <path d="M1.5 3L4.5 6L7.5 3" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>
    </svg>
    {showAllShortcuts ? 'Show less' : `${shortcuts.length - visibleShortcuts.length} more`}
  </button>

  <div class="sep"></div>

  <div class="section-title">Settings</div>
  <div class="setting-row">
    <label for="undo-limit">Undo history depth</label>
    <input id="undo-limit" type="number" min="1" max="50" value={$undoLimit} on:change={clampUndoLimit} />
  </div>

  <div class="setting-row">
    <label for="allow-multi-capture">Allow multiple captures of the same parameter</label>
    <label class="switch">
      <input id="allow-multi-capture" type="checkbox" checked={$allowMultipleCaptures}
          on:change={e => allowMultipleCaptures.set(e.target.checked)} />
      <span class="switch-track"></span>
    </label>
  </div>

  <div class="setting-row">
    <span class="setting-label">Theme</span>
    <div class="theme-toggle">
      <button class:active={$theme === 'dark'}  on:click={() => setTheme('dark')}>Dark</button>
      <button class:active={$theme === 'light'} on:click={() => setTheme('light')}>Light</button>
    </div>
  </div>
</div>

<style>
  .panel {
    position: absolute;
    top: 28px;
    right: 8px;
    width: 320px;
    max-height: calc(100vh - 40px);
    overflow-y: auto;
    background: var(--panel-bg);
    border: 1px solid var(--grid);
    border-radius: 6px;
    padding: 10px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
    z-index: 1000;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }

  .section-title {
    font-size: 10px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: rgba(var(--text-rgb), 0.43);
  }

  .shortcuts {
    display: flex;
    flex-direction: column;
    gap: 5px;
  }

  .row {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 10px;
  }

  .keys {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 3px;
    flex-shrink: 0;
    max-width: 45%;
  }

  .plus {
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.36);
  }

  .kbd {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 16px;
    height: 16px;
    padding: 0 4px;
    border: 1px solid var(--grid);
    border-radius: 3px;
    font-size: 10px;
    font-family: 'Segoe UI Mono', Consolas, monospace;
    color: var(--text);
  }

  .desc {
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.58);
    text-align: right;
  }

  /* Fade mask over the last row when collapsed — implies there's more below
     without a hard cutoff. Applied to .shortcuts itself (not a wrapper) since
     that's exactly the box whose bottom edge is doing the collapsing. */
  .shortcuts.fade {
    position: relative;
    padding-bottom: 6px;
  }
  .shortcuts.fade::after {
    content: '';
    position: absolute;
    left: 0; right: 0; bottom: 0;
    height: 22px;
    background: linear-gradient(to bottom, rgba(var(--panel-bg-rgb), 0), rgba(var(--panel-bg-rgb), 1));
    pointer-events: none;
  }

  .show-more {
    align-self: center;
    display: flex;
    align-items: center;
    gap: 5px;
    margin-top: -4px;
    padding: 3px 10px;
    border: 1px solid var(--grid);
    border-radius: 12px;
    background: var(--panel-bg);
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.3);
    color: rgba(var(--text-rgb), 0.7);
    font-size: 10px;
    font-family: inherit;
    cursor: pointer;
    z-index: 1;
  }
  .show-more:hover { color: var(--accent-light); border-color: var(--accent); }
  .show-more svg { transition: transform 0.2s ease; }
  .show-more svg.open { transform: rotate(180deg); }

  .sep {
    height: 1px;
    background: var(--grid);
  }

  .setting-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
  }
  .setting-row label, .setting-row .setting-label {
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.73);
  }
  /* Toggle switch — a plain <input type="checkbox"> made invisible but kept
     in place (so it still gets focus/keyboard/click), with a track+knob span
     drawn on top and animated off its :checked state. */
  .switch {
    position: relative;
    display: inline-block;
    width: 32px;
    height: 18px;
    flex-shrink: 0;
    cursor: pointer;
  }
  .switch input {
    position: absolute;
    inset: 0;
    margin: 0;
    opacity: 0;
    cursor: pointer;
  }
  .switch-track {
    position: absolute;
    inset: 0;
    background: var(--grid);
    border: 1px solid var(--grid);
    border-radius: 999px;
    transition: background 0.15s, border-color 0.15s;
  }
  .switch-track::before {
    content: '';
    position: absolute;
    top: 2px;
    left: 2px;
    width: 14px;
    height: 14px;
    border-radius: 50%;
    background: rgba(var(--text-rgb), 0.55);
    transition: transform 0.15s ease, background 0.15s;
  }
  .switch input:checked + .switch-track {
    background: rgba(var(--accent-rgb), 0.35);
    border-color: var(--accent);
  }
  .switch input:checked + .switch-track::before {
    transform: translateX(14px);
    background: var(--accent-light);
  }
  .switch input:focus-visible + .switch-track {
    outline: 2px solid var(--accent);
    outline-offset: 1px;
  }
  .setting-row input:not([type='checkbox']) {
    width: 48px;
    height: 20px;
    background: var(--bg);
    border: 1px solid var(--grid);
    border-radius: 3px;
    color: var(--text);
    font-size: 11px;
    font-family: inherit;
    text-align: center;
    padding: 0 4px;
  }
  .setting-row input:focus { border-color: var(--accent); outline: none; }

  .theme-toggle {
    display: flex;
    gap: 4px;
  }
  .theme-toggle button {
    height: 20px;
    padding: 0 8px;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.58);
    font-size: 10px;
    font-weight: 600;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .theme-toggle button:hover  { border-color: var(--border); color: rgba(var(--text-rgb), 0.73); }
  .theme-toggle button.active { border-color: rgba(var(--accent-rgb), 0.4); color: var(--accent-light); }
</style>
