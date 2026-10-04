<script>
  import { createEventDispatcher } from 'svelte'
  import { postToCs } from './ipc.js'
  import SectionHead from './SectionHead.svelte'
  const dispatch = createEventDispatcher()

  // The Path Mapper's rule list — one component for both the row (inline) and
  // the fullscreen view. Always-editable Source | Target grid with a single
  // Apply button (2026-10-04 kullanıcı: "edit rules diye ayrıca tıklamama
  // gerek olmamalı", Cancel/Revert/manual uyarısı da yok). Persisted fields go
  // out through `patch`; the host decides how to store them (row:
  // Pane.patchSlider, fullscreen: syncControl).
  export let slider = {}   // { id, sourceId?, mapping?, mapperManual?, mapperOpen? }
  // Editing controls (+ / − / Revert / Apply, writable fields) only exist in the
  // app's Edit mode, like every other row's × and settings buttons
  // (2026-10-04 kullanıcı: "sadece edit modunda olsun, daha temiz olur"). Their
  // slots stay reserved in Preview so the row doesn't change height between modes.
  export let mode = 'preview'
  $: editing = mode === 'edit'

  $: mapping = slider.mapping ?? []
  $: open    = slider.mapperOpen !== false

  // Rules arrive live (every drag/solve rewrites them), so a separate draft is
  // kept: until the user touches a field the rows just follow the live
  // mapping; the first edit pins the draft (`dirty`) so an incoming update
  // can't overwrite what's being typed.
  const liveRows = m => m.length ? m.map(r => ({ source: r.source, target: r.target })) : [{ source: '', target: '' }]
  // `$: mapping` hasn't run yet when this initializer does (reactive
  // statements execute after script init), so read the prop directly — using
  // `mapping` here was undefined and crashed the whole UI on render.
  let rows = liveRows(slider.mapping ?? [])
  let dirty = false
  let awaiting = false   // Apply sent, waiting for the mapper's own push to confirm

  $: liveKey = JSON.stringify(mapping)
  let lastKey = liveKey
  $: if (liveKey !== lastKey) { lastKey = liveKey; if (awaiting) { dirty = false; awaiting = false } }
  $: if (!dirty) rows = liveRows(mapping)

  $: cleaned = rows
    .map(r => ({ source: r.source.trim(), target: r.target.trim() }))
    .filter(r => r.source && r.target)
  $: changed = cleaned.length !== mapping.length ||
    cleaned.some((r, i) => r.source !== mapping[i].source || r.target !== mapping[i].target)

  function touch()       { dirty = true }
  function addRule()     { rows = [...rows, { source: '', target: '' }]; dirty = true }
  function removeRule(i) { rows = rows.filter((_, k) => k !== i); dirty = true }

  // Hand-written rules make the graph's reorder state (mapperOrders) stale and
  // a drag would overwrite them, so the host turns dragging off while
  // `mapperManual` is set; applying an empty list (pass-through) turns it back on.
  function applyRules() {
    if (!changed) return
    dispatch('patch', { mapperOrders: {}, mapperManual: cleaned.length > 0 })
    postToCs({ type: 'pathMapper_setLexers', id: slider.sourceId ?? slider.id, lexers: cleaned })
    awaiting = true
  }
  // Drops the draft: rows go back to following the live rules.
  function revert() { rows = liveRows(mapping); dirty = false; awaiting = false }
  // Leaving Edit mode drops an unapplied draft — nothing in Preview could
  // show or apply it, so it would just sit there invisibly.
  $: if (!editing && dirty) revert()
  function toggleOpen() { dispatch('patch', { mapperOpen: !open }) }
</script>

<!-- Row title already says "Path Mapper", so the section is just "Rules". Count
     shows on the right only when collapsed (open, the rows are the count). -->
<SectionHead label="Rules" {open}
  note={open ? (mapping.length ? '' : 'pass-through') : (mapping.length ? `${mapping.length} rule${mapping.length === 1 ? '' : 's'}` : 'pass-through')}
  on:click={toggleOpen} />

{#if open}
  <div class="mapper-body">
    <div class="grid head"><span>Source</span><span>Target</span><span></span></div>
    {#each rows as r, i}
      <div class="grid">
        <input class="cell" readonly={!editing} bind:value={r.source} on:input={touch} placeholder={'{A;B}'} spellcheck="false" />
        <input class="cell" readonly={!editing} bind:value={r.target} on:input={touch} placeholder={'{A;B}'} spellcheck="false" />
        {#if editing}
          <button class="icon-btn" on:click={() => removeRule(i)} title="Remove rule" aria-label="Remove rule">
            <svg width="8" height="8" viewBox="0 0 8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><path d="M0.5 4h7"/></svg>
          </button>
        {:else}
          <span></span>
        {/if}
      </div>
    {/each}
    <!-- Same 3-column grid as the rule rows: Revert/Apply end flush with the
         Target box's right edge, "+" sits in the × column right under the
         last rule's ×. -->
    <div class="grid actions">
      {#if editing}
        <div class="btns">
          <button class="mini" disabled={!dirty} on:click={revert} title="Discard your edits and go back to the current rules">Revert</button>
          <button class="mini primary" disabled={!changed} on:click={applyRules}>Apply</button>
        </div>
        <button class="icon-btn" on:click={addRule} title="Add rule" aria-label="Add rule">
          <svg width="8" height="8" viewBox="0 0 8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><path d="M4 0.5v7M0.5 4h7"/></svg>
        </button>
      {/if}
    </div>
  </div>
{/if}

<style>

  /* Fixed 22px pitch (half a slider, MODULE 44 / 2): column head, every rule
     row and the action row are each exactly 22px, with 11px above and below.
     Collapsed-Graph Path Mapper row with ONE rule = 44 (header) + 22 (section
     head) + 11 + 22 + 22 + 22 + 11 + 22 (Graph head) = 176 = 4 sliders; every
     extra rule adds exactly half a slider (2026-10-04 kullanıcı). */
  /* Right padding 8px + a 20px last column = the row's own × column (edit mode:
     × is 20px wide, 8px from the edge), so the − / + sit right under it and the
     Target box / Apply end exactly where the fullscreen button ends. */
  .mapper-body { padding: 11px 8px 11px 12px; display: flex; flex-direction: column; }
  .grid { display: grid; grid-template-columns: 1fr 1fr 20px; gap: 4px; align-items: center; height: 22px; }
  .grid.head { font-size: 9px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; color: rgba(var(--text-rgb), 0.4); }

  .cell {
    min-width: 0;
    height: 18px;
    padding: 0 6px;
    border: 1px solid var(--border);
    border-radius: 4px;
    background: var(--grid);
    color: var(--text);
    font-family: 'Segoe UI Mono', 'Cascadia Mono', Consolas, monospace;
    font-size: 10px;
    box-sizing: border-box;
  }
  input.cell:focus { outline: none; border-color: var(--accent); }
  input.cell[readonly] { cursor: default; color: rgba(var(--text-rgb), 0.8); }
  input.cell[readonly]:focus { border-color: var(--border); }

  /* Small boxed button for the × / + column — same look as the row-level
     icon buttons (var(--grid) box, 4px radius), 18px to match the inputs. */
  .icon-btn {
    justify-self: center;
    width: 18px;
    height: 18px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0;
    border: none;
    border-radius: 4px;
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.5);
    cursor: pointer;
    transition: background 0.1s, color 0.15s;
  }
  .icon-btn:hover { background: var(--border); color: rgba(var(--text-rgb), 0.85); }

  .actions .btns { grid-column: 1 / 3; display: flex; justify-content: flex-end; gap: 6px; }
  .mini {
    height: 20px;
    padding: 0 10px;
    border: none;
    border-radius: 4px;
    background: var(--grid);
    color: rgba(var(--text-rgb), 0.7);
    font: inherit;
    font-size: 10px;
    font-weight: 600;
    cursor: pointer;
  }
  .mini:hover { background: var(--border); color: rgba(var(--text-rgb), 0.95); }
  .mini.primary { background: var(--accent); color: #fff; }
  .mini.primary:hover:not(:disabled) { filter: brightness(1.1); }
  .mini:disabled { opacity: 0.4; cursor: default; }
</style>
