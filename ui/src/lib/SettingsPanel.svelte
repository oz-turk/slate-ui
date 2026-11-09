<script>
  import { settingsOpen, theme, allowMultipleCaptures, showParamIcons, accent, ACCENTS, iconCatalog, hiddenIconTypes, ICON_GROUPS } from '../stores/uiState.js'
  import { undoLimit } from '../stores/history.js'
  import { postStateSnapshot, postToCs } from './ipc.js'
  import { clickOutside } from './actions.js'
  import Switch from './Switch.svelte'

  // Per-type icon list (Show icons): groups expand into a table of icon / name /
  // eye toggle. The catalog is asked from C# the first time it is needed.
  let catalogAsked = false
  $: if ($showParamIcons && !catalogAsked && !$iconCatalog.length) {
    catalogAsked = true
    postToCs({ type: 'icon_catalog_request' })
  }
  let openGroups = {}
  let view = 'main'
  $: byGroup = Object.fromEntries(ICON_GROUPS.map(g => [g.id,
    $iconCatalog.filter(i => i.group === g.id).sort((a, b) => a.name.localeCompare(b.name))]))
  const isShown = (guid, hidden) => !hidden.includes(guid)
  function setTypes(guids, show) {
    hiddenIconTypes.update(h => show ? h.filter(x => !guids.includes(x)) : [...new Set([...h, ...guids])])
  }

  // Press-and-drag toggling: the press decides the target state (opposite of
  // what was clicked), every other eye the pointer enters while the button is
  // still down gets set to that same state. Released anywhere = done.
  let painting = null
  function startPaintType(guid) {
    painting = !isShown(guid, $hiddenIconTypes)
    setTypes([guid], painting)
  }
  function overPaintType(guid) {
    if (painting !== null) setTypes([guid], painting)
  }
  function startPaintGroup(id) {
    const guids = byGroup[id].map(i => i.guid)
    painting = !guids.some(g => !$hiddenIconTypes.includes(g))
    setTypes(guids, painting)
  }
  function overPaintGroup(id) {
    if (painting !== null) setTypes(byGroup[id].map(i => i.guid), painting)
  }

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
    { keys: ['Alt', 'drag'],  desc: 'Copy a control or group instead of moving it', critical: true },
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

<svelte:window on:mouseup={() => painting = null} />

<div class="panel" data-vp-overlay use:clickOutside={{ onClose: () => settingsOpen.set(false), exclude: '[data-settings-toggle]' }}>
  {#if view === 'icons'}
    <div class="sub-head">
      <button class="back" on:click={() => view = 'main'}>
        <svg width="9" height="9" viewBox="0 0 9 9" fill="none">
          <path d="M6 1.5L3 4.5L6 7.5" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        Settings
      </button>
      <span class="sub-title">Show icons</span>
    </div>
    <div class="icon-groups">
      {#each ICON_GROUPS as g}
        {@const items = byGroup[g.id] ?? []}
        {#if items.length}
          {@const shownCount = items.filter(i => isShown(i.guid, $hiddenIconTypes)).length}
          <div class="ig-head">
            <button class="ig-toggle" on:click={() => openGroups = { ...openGroups, [g.id]: !openGroups[g.id] }}>
              <svg class:open={openGroups[g.id]} width="9" height="9" viewBox="0 0 9 9" fill="none">
                <path d="M3 1.5L6 4.5L3 7.5" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>
              </svg>
              <span class="ig-name">{g.label}</span>
              <span class="ig-count">{shownCount}/{items.length}</span>
            </button>
            <button class="eye" class:active={shownCount > 0} title={shownCount > 0 ? 'Hide all' : 'Show all'}
                on:mousedown|preventDefault={() => startPaintGroup(g.id)}
                on:mouseenter={() => overPaintGroup(g.id)}
                on:click={e => { if (e.detail === 0) setTypes(byGroup[g.id].map(i => i.guid), shownCount === 0) }}>
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                {#if shownCount > 0}
                  <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8Z" /><circle cx="12" cy="12" r="3" />
                {:else}
                  <path d="M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49" />
                  <path d="M14.084 14.158a3 3 0 0 1-4.242-4.242" />
                  <path d="M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143" />
                  <path d="m2 2 20 20" />
                {/if}
              </svg>
            </button>
          </div>
          {#if openGroups[g.id]}
            <div class="ig-grid">
              {#each items as it (it.guid)}
                {@const on = isShown(it.guid, $hiddenIconTypes)}
                <button class="ig-tile" class:on data-name={it.name}
                    on:mousedown|preventDefault={() => startPaintType(it.guid)}
                    on:mouseenter={() => overPaintType(it.guid)}
                    on:click={e => { if (e.detail === 0) setTypes([it.guid], !on) }}>
                  {#if it.icon}<img src={it.icon} alt="" draggable="false" />{:else}<span class="ig-fallback">{it.name.slice(0, 2)}</span>{/if}
                </button>
              {/each}
            </div>
          {/if}
        {/if}
      {/each}
    </div>
  {:else}
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
    <Switch label="Allow multiple captures of the same parameter" checked={$allowMultipleCaptures}
        on:change={e => allowMultipleCaptures.set(e.target.checked)} />
  </div>

  <div class="setting-row">
    <Switch label="Show icons" checked={$showParamIcons}
        on:change={e => { showParamIcons.set(e.target.checked); postStateSnapshot() }} />
  </div>

  {#if $showParamIcons}
    <button class="nav-row" on:click={() => view = 'icons'}>
      <span class="nav-label">Choose icons…</span>
      {#if $iconCatalog.length}
        <span class="nav-count">{$iconCatalog.filter(i => isShown(i.guid, $hiddenIconTypes)).length} of {$iconCatalog.length} shown</span>
      {/if}
      <svg width="10" height="10" viewBox="0 0 9 9" fill="none">
        <path d="M3 1.5L6 4.5L3 7.5" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>
      </svg>
    </button>
  {/if}


  <div class="setting-row">
    <span class="setting-label">Accent</span>
    <div class="swatches">
      {#each ACCENTS as a}
        <button class="swatch" class:active={$accent === a.id} title={a.label}
            style="--sw: {$theme === 'light' ? a.light : a.dark}"
            on:click={() => accent.set(a.id)}></button>
      {/each}
    </div>
  </div>

  <div class="setting-row">
    <span class="setting-label">Theme</span>
    <div class="theme-toggle">
      <button class:active={$theme === 'dark'}  on:click={() => setTheme('dark')}>Dark</button>
      <button class:active={$theme === 'light'} on:click={() => setTheme('light')}>Light</button>
    </div>
  </div>
  {/if}
</div>

<style>
  .swatches { display: flex; gap: 7px; }
  .swatch { width: 16px; height: 16px; border-radius: 50%; background: var(--sw); border: 2px solid transparent; padding: 0; cursor: pointer; box-shadow: 0 0 0 1px var(--grid); transition: transform 0.1s; }
  .swatch:hover { transform: scale(1.15); }
  .swatch.active { border-color: var(--panel-bg); box-shadow: 0 0 0 2px var(--text); }
  :global(:root[data-theme="dark"]) .panel  { --tone: rgba(0, 0, 0, 0.22); --tone-hover: rgba(255, 255, 255, 0.07); --tone-on: rgba(255, 255, 255, 0.12); }
  :global(:root[data-theme="light"]) .panel { --tone: rgba(0, 0, 0, 0.045); --tone-hover: rgba(0, 0, 0, 0.09); --tone-on: rgba(0, 0, 0, 0.13); }
  .nav-row { display: flex; align-items: center; gap: 8px; width: 100%; box-sizing: border-box; margin: 2px 0 4px; padding: 6px 10px; background: var(--tone); border: 1px solid var(--grid); border-radius: 5px; color: var(--text); font-size: 11px; cursor: pointer; text-align: left; transition: background 0.12s, border-color 0.12s; }
  .nav-row:hover { background: var(--tone-hover); border-color: var(--text-dim, #888); }
  .nav-label { flex: 1; font-weight: 600; }
  .nav-count { color: var(--text-dim, #888); font-size: 10px; }
  .back { display: flex; align-items: center; gap: 5px; background: none; border: none; color: var(--text-dim, #888); cursor: pointer; font-size: 11px; padding: 2px 4px; border-radius: 4px; }
  .back:hover { color: var(--text); }
  .sub-head { display: flex; align-items: center; gap: 10px; padding-bottom: 6px; border-bottom: 1px solid var(--grid); margin-bottom: 4px; }
  .sub-title { font-size: 12px; font-weight: 600; color: var(--text); }
  .icon-groups { display: flex; flex-direction: column; gap: 4px; padding: 2px 0 6px 0; }
  .ig-head { display: flex; align-items: stretch; min-height: 34px; padding-right: 6px; background: var(--tone); border: 1px solid var(--grid); border-radius: 6px; transition: background 0.12s, border-color 0.12s; }
  .ig-head:hover { background: var(--tone-hover); border-color: var(--text-dim, #888); }
  .ig-head .eye { align-self: center; }
  .ig-toggle { display: flex; align-items: center; gap: 8px; flex: 1; background: none; border: none; color: var(--text); font-size: 12px; font-weight: 600; cursor: pointer; padding: 0 6px 0 10px; text-align: left; }
  .ig-toggle svg { transition: transform 0.12s; }
  .ig-toggle svg.open { transform: rotate(90deg); }
  .ig-count { color: var(--text-dim, #888); font-size: 11px; font-weight: 400; margin-left: auto; padding-right: 6px; }
  .ig-grid { display: grid; grid-template-columns: repeat(6, 1fr); gap: 4px; padding: 4px 0 8px 0; }
  .ig-tile { position: relative; aspect-ratio: 1; display: flex; align-items: center; justify-content: center; background: var(--tone); border: 1px solid var(--grid); border-radius: 5px; padding: 0; cursor: pointer; transition: background 0.12s, border-color 0.12s; }
  .ig-tile img { width: 24px; height: 24px; opacity: 0.3; filter: grayscale(1); pointer-events: none; }
  .ig-tile.on { background: var(--tone-on); border-color: var(--accent); }
  .ig-tile.on img { opacity: 1; filter: none; }
  .ig-tile:hover { background: var(--tone-hover); border-color: var(--text-dim, #888); }
  .ig-tile.on:hover { border-color: var(--accent); }
  .ig-fallback { font-size: 10px; color: var(--text-dim, #888); pointer-events: none; }
  /* Instant name tooltip (native title has a long delay). First/last column
     anchor to their own edge so it never runs off the panel. */
  .ig-tile:hover::after {
    content: attr(data-name);
    position: absolute; bottom: calc(100% + 4px); left: 50%; transform: translateX(-50%);
    white-space: nowrap; font-size: 10px; padding: 2px 6px; border-radius: 3px;
    background: var(--panel-bg); color: var(--text); border: 1px solid var(--grid);
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4); z-index: 20; pointer-events: none;
  }
  .ig-tile:nth-child(6n+1):hover::after { left: 0; transform: none; }
  .ig-tile:nth-child(6n):hover::after { left: auto; right: 0; transform: none; }
  .eye { width: 22px; height: 20px; display: flex; align-items: center; justify-content: center; background: transparent; border: 1px solid transparent; border-radius: 4px; color: var(--text-dim, #888); cursor: pointer; padding: 0; }
  .eye:hover { color: var(--text); border-color: var(--border); }
  .eye.active { background: var(--tone-on); border-color: var(--grid); color: var(--text); }
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
