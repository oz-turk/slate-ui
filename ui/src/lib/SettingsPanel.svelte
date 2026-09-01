<script>
  import { settingsOpen, theme } from '../stores/uiState.js'
  import { undoLimit } from '../stores/history.js'
  import { postStateSnapshot } from './ipc.js'
  import { clickOutside } from './actions.js'

  const shortcuts = [
    { keys: ['Tab'],          desc: 'Toggle Edit / Preview' },
    { keys: ['Click'],        desc: 'Select a control (edit mode)' },
    { keys: ['Shift/Ctrl', 'Click'], desc: 'Add to selection' },
    { keys: ['Ctrl', 'Shift', 'Click'], desc: 'Select range from last click' },
    { keys: ['Esc'],          desc: 'Clear selection' },
    { keys: ['Drag'],         desc: 'Reorder / move controls' },
    { keys: ['x'],            desc: 'Delete control / ungroup group under mouse' },
    { keys: ['c'],            desc: 'Capture into pane under mouse' },
    { keys: ['g'],            desc: 'Group selected controls' },
    { keys: ['Right-click'],  desc: 'Pane menu — split / close' },
    { keys: ['Corner drag'],  desc: 'Split pane, or drag out to collapse a neighbour' },
    { keys: ['Ctrl', '1–9'],  desc: 'Switch workspace' },
    { keys: ['Ctrl', 'Z'],    desc: 'Undo' },
    { keys: ['Ctrl', 'Scroll'], desc: 'Zoom the UI (WebView2 default)' },
    { keys: ['Ctrl', 'drag'], desc: 'Snap resize to slider-row size' },
    { keys: ['Alt', 'window corner drag'], desc: 'Add a pane at the edge without disturbing the rest of the layout' },
    { keys: ['Edge drag'],          desc: 'Aligned edges move together automatically' },
    { keys: ['Alt', 'edge drag'],   desc: 'Break this edge off the group (or just shake it)' },
  ]

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
  <div class="shortcuts">
    {#each shortcuts as s}
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

  <div class="sep"></div>

  <div class="section-title">Settings</div>
  <div class="setting-row">
    <label for="undo-limit">Undo history depth</label>
    <input id="undo-limit" type="number" min="1" max="50" value={$undoLimit} on:change={clampUndoLimit} />
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
    width: 260px;
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
    align-items: center;
    justify-content: space-between;
    gap: 10px;
  }

  .keys {
    display: flex;
    align-items: center;
    gap: 3px;
    flex-shrink: 0;
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
  .setting-row input {
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
