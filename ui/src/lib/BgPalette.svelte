<script>
  // Background colour picker shared by group headers (TextSettingsPopup) and
  // pane backgrounds (PaneSettingsPopup): a pastel preset grid + Custom… +
  // Reset. Catppuccin Frappe, same set as the Obsidian vault's AnuPpuccin
  // theme (Teal = the vault's own custom #11B7C5, not Frappe's #81c8be).
  // Presets apply at one fixed low alpha so they read as a tint; anything
  // else (hue/alpha) goes through Custom, which the HOST opens — the nested
  // ColourPickerPopup has to be a DOM descendant of the host popup (see
  // their clickOutside notes), so this component only reports the click.
  import { createEventDispatcher } from 'svelte'
  const dispatch = createEventDispatcher()

  export let value = null   // '#RRGGBBAA' | null — current colour, for the active highlight + Reset

  const PRESET_ALPHA = '47'   // ~28%
  const PALETTE = [
    ['Rosewater', 'f2d5cf'], ['Flamingo', 'eebebe'], ['Pink',     'f4b8e4'], ['Mauve',   'ca9ee6'],
    ['Red',       'e78284'], ['Maroon',   'ea999c'], ['Peach',    'ef9f76'], ['Yellow',  'e5c890'],
    ['Green',     'a6d189'], ['Teal',     '11b7c5'], ['Sky',      '99d1db'], ['Sapphire','85c1dc'],
    ['Blue',      '8caaee'], ['Lavender', 'babbf1'],
  ]
  $: activeRgb = value ? value.slice(1, 7).toLowerCase() : null
</script>

<div class="palette">
  {#each PALETTE as [name, rgb]}
    <button class="sw" class:active={activeRgb === rgb} title={name}
      style="background: linear-gradient(#{rgb}{PRESET_ALPHA}, #{rgb}{PRESET_ALPHA}), var(--bg); --edge: #{rgb}"
      on:click={() => dispatch('pick', '#' + rgb + PRESET_ALPHA)}></button>
  {/each}
</div>
<div class="actions">
  <button on:click={e => dispatch('custom', e.currentTarget.getBoundingClientRect())}>Custom…</button>
  {#if value}<button on:click={() => dispatch('reset')}>Reset</button>{/if}
</div>

<style>
  .palette {
    display: grid;
    grid-template-columns: repeat(7, 1fr);
    gap: 4px;
    padding: 0 4px;
  }
  .sw {
    aspect-ratio: 1;
    border-radius: 4px;
    border: 1px solid var(--grid);
    border-bottom: 2px solid var(--edge);
    padding: 0;
    cursor: pointer;
  }
  .sw:hover  { border-color: var(--edge); }
  .sw.active { outline: 1px solid var(--edge); outline-offset: 1px; }

  .actions {
    display: flex;
    gap: 4px;
    padding: 0 4px;
  }
  .actions button {
    flex: 1;
    height: 20px;
    padding: 0;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.5);
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .actions button:hover { color: rgba(var(--text-rgb), 0.75); border-color: var(--border); }
</style>
