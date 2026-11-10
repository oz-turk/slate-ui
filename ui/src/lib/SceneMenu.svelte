<script>
  import { scenes, activeSceneId, sceneModified, applyingSceneId } from '../stores/sceneStore.js'
  import { saveScene, overwriteScene, renameScene, deleteScene, applyScene } from '../stores/scenes.js'
  import { hoverHint, showSceneChanges } from '../stores/uiState.js'
  import { clickOutside } from './actions.js'
  import Switch from './Switch.svelte'

  let open = false
  let newName = ''
  let editingId = null
  let editValue = ''
  let note = ''

  $: active = $scenes.find(s => s.id === $activeSceneId)

  // Dot next to the active scene's name when live values no longer match it
  // (shared, debounced computation in scenes.js).
  $: modified = $sceneModified

  // The toolbar clips its children (overflow: hidden), so the popup is
  // position: fixed and placed from the button's rect instead of absolute.
  let btnEl
  let popupTop = 0
  let popupRight = 0

  function toggle() {
    open = !open; note = ''; editingId = null
    if (open && btnEl) {
      const r = btnEl.getBoundingClientRect()
      popupTop = r.bottom + 4
      popupRight = Math.max(4, window.innerWidth - r.right)
    }
  }
  function close() { open = false; editingId = null }

  function apply(id) {
    const { skipped } = applyScene(id)
    note = skipped ? `${skipped} control${skipped === 1 ? '' : 's'} not found` : ''
  }
  function save() {
    saveScene(newName)
    newName = ''
    note = ''
  }
  function startRename(s) { editingId = s.id; editValue = s.name }
  function commitRename() {
    if (editingId) renameScene(editingId, editValue)
    editingId = null
  }
  function onRenameKey(e) {
    if (e.key === 'Enter')  { commitRename(); e.preventDefault() }
    if (e.key === 'Escape') { editingId = null; e.stopPropagation() }
  }
  function onSaveKey(e) {
    if (e.key === 'Enter') { save(); e.preventDefault() }
    e.stopPropagation()   // typing must not trigger global shortcuts (Tab, x, c ...)
  }
  function focusAndSelect(node) { node.focus(); node.select() }
</script>

  <div class="scene-menu" use:clickOutside={{ onClose: close }}>
    <button class="scene-btn" class:active={open || !!active} bind:this={btnEl} on:click={toggle}
        on:mouseenter={() => hoverHint.set('Scenes: saved sets of control values')}
        on:mouseleave={() => hoverHint.set(null)}>
      <span class="label">{active ? active.name : 'Scenes'}</span>
      {#if $applyingSceneId}<span class="spinner" title="Applying scene — Grasshopper is solving"></span>
      {:else if active && modified}<span class="dot" title="Values differ from this scene"></span>{/if}
      <svg width="8" height="8" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6" /></svg>
    </button>

    {#if open}
      <div class="popup" style="top: {popupTop}px; right: {popupRight}px">
        {#each $scenes as s (s.id)}
          <div class="item" class:current={s.id === $activeSceneId}>
            {#if editingId === s.id}
              <input class="rename" bind:value={editValue} use:focusAndSelect
                on:blur={commitRename} on:keydown={onRenameKey} />
            {:else}
              <button class="name" on:click={() => apply(s.id)}
                on:dblclick={() => startRename(s)}
                title="Click: apply  ·  Double-click: rename">{s.name}</button>
              {#if s.id === $applyingSceneId}<span class="spinner" title="Applying — Grasshopper is solving"></span>
              {:else if s.id === $activeSceneId && modified}<span class="dot" title="Values differ from this scene"></span>{/if}
              <button class="icon" title="Overwrite with current values" on:click={() => overwriteScene(s.id)}>
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" /><path d="M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7" /><path d="M7 3v4a1 1 0 0 0 1 1h7" /></svg>
              </button>
              <button class="icon del" title="Delete scene" on:click={() => deleteScene(s.id)}>×</button>
            {/if}
          </div>
        {/each}

        {#if $scenes.length === 0}
          <div class="empty">No scenes yet</div>
        {/if}

        <div class="opt">
          <Switch bind:checked={$showSceneChanges} label="Show changes"
            title="Show a dot next to every control whose value differs from the active scene">
            <span class="legend-dot"></span>
          </Switch>
        </div>

        <div class="save-row">
          <input class="new-name" placeholder="Scene name" bind:value={newName} on:keydown={onSaveKey} />
          <button class="save" on:click={save} title="Save current values as a new scene">Save</button>
        </div>

        {#if note}<div class="note">{note}</div>{/if}
      </div>
    {/if}
  </div>

<style>
  .scene-menu { position: relative; flex-shrink: 0; }

  .scene-btn {
    display: flex;
    align-items: center;
    gap: 5px;
    height: 18px;
    max-width: 130px;
    padding: 0 7px;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.58);
    font-size: 10px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .scene-btn:hover  { border-color: var(--border); color: rgba(var(--text-rgb), 0.73); }
  .scene-btn.active { border-color: rgba(var(--accent-rgb), 0.4); color: var(--accent-light); }
  .label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

  /* Modified marker: sits in a flex row with align-items: center, so it is
     vertically centred against the name in both the button and the list row. */
  .dot {
    width: 5px;
    height: 5px;
    margin: 0 3px;
    border-radius: 50%;
    flex-shrink: 0;
    background: var(--accent-light);
  }
  /* Scene still solving in GH: takes the dot's place while it runs. */
  .spinner {
    width: 8px;
    height: 8px;
    margin: 0 1px;
    box-sizing: border-box;
    border-radius: 50%;
    flex-shrink: 0;
    border: 1.5px solid rgba(var(--accent-rgb), 0.25);
    border-top-color: var(--accent);
    animation: scene-spin 0.7s linear infinite;
  }
  @keyframes scene-spin { to { transform: rotate(360deg); } }

  .popup {
    position: fixed;
    z-index: 1000;
    /* Fixed, not content-sized: applying a scene adds the spinner/changed
       dots, and a menu that grew leftward (it is right-anchored) outran the
       viewport cut-out, which C# can't update until the solve ends. */
    width: 260px;
    max-height: 320px;
    overflow-y: auto;
    padding: 4px;
    background: var(--panel-bg);
    border: 1px solid var(--grid);
    border-radius: 4px;
    box-shadow: 0 6px 18px rgba(0, 0, 0, 0.35);
    display: flex;
    flex-direction: column;
    gap: 1px;
  }

  .item { display: flex; align-items: center; gap: 2px; border-radius: 3px; }
  .item:hover { background: var(--grid); }
  .item.current { background: rgba(var(--accent-rgb), 0.25); }

  .name {
    flex: 1;
    min-width: 0;
    text-align: left;
    padding: 3px 7px;
    border: none;
    background: transparent;
    color: rgba(var(--text-rgb), 0.75);
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .item:hover .name, .item.current .name { color: var(--text); }

  .icon {
    display: flex; align-items: center; justify-content: center;
    width: 16px; height: 16px;
    padding: 0;
    border: none;
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.36);
    font-size: 12px;
    line-height: 1;
    cursor: pointer;
  }
  .icon:hover { color: var(--text); background: rgba(var(--text-rgb), 0.1); }

  .rename, .new-name {
    flex: 1;
    min-width: 0;
    background: var(--panel-bg);
    border: 1px solid rgba(var(--accent-rgb), 0.53);
    border-radius: 3px;
    color: var(--text);
    font-size: 11px;
    font-family: inherit;
    padding: 2px 5px;
    outline: none;
  }
  .new-name { border-color: var(--grid); }
  .new-name:focus { border-color: rgba(var(--accent-rgb), 0.53); }

  .empty, .note {
    padding: 3px 7px;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.45);
  }

  /* Same dot as SceneDot.svelte, as a legend for what the switch turns on. */
  .legend-dot {
    display: inline-block;
    width: 5px;
    height: 5px;
    margin-left: 5px;
    vertical-align: middle;
    border-radius: 50%;
    background: var(--accent-light);
    opacity: 0.85;
  }

  .opt {
    padding: 5px 7px 3px;
    margin-top: 2px;
    border-top: 1px solid var(--grid);
  }

  .save-row { display: flex; gap: 4px; padding: 3px 3px 1px; margin-top: 2px; border-top: 1px solid var(--grid); }
  .save {
    padding: 0 8px;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.75);
    font-size: 10px;
    font-family: inherit;
    cursor: pointer;
  }
  .save:hover { color: var(--text); border-color: var(--border); }
</style>
