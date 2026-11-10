<script>
  // The number readout next to a popup slider (font size, colour channels,
  // pattern scale/opacity). Click it to type a value: Enter or leaving the
  // field applies it (clamped to min/max), Esc cancels. Same look as the
  // plain readout it replaces.
  import { createEventDispatcher, tick } from 'svelte'
  const dispatch = createEventDispatcher()

  export let value              // number shown
  export let min = -Infinity
  export let max = Infinity
  export let decimals = 0       // shown and typed precision
  export let suffix = ''        // 'px', '%', '°', 'x'

  let editing = false
  let text = ''
  let input

  $: shown = Number(value).toFixed(decimals)

  async function start() {
    text = shown
    editing = true
    await tick()
    input?.focus()
    input?.select()
  }

  function commit() {
    if (!editing) return
    editing = false
    const n = parseFloat(String(text).replace(',', '.'))   // Turkish keyboards type a comma
    if (!Number.isFinite(n)) return
    const f = 10 ** decimals
    dispatch('commit', Math.min(max, Math.max(min, Math.round(n * f) / f)))
  }

  function onKey(e) {
    e.stopPropagation()   // typing must not trigger global shortcuts (Tab, x, c ...)
    if (e.key === 'Enter') { e.preventDefault(); commit() }
    else if (e.key === 'Escape') { e.preventDefault(); editing = false }
  }
</script>

{#if editing}
  <input class="value edit" bind:this={input} bind:value={text}
    on:keydown={onKey} on:blur={commit} on:pointerdown|stopPropagation />
{:else}
  <button class="value" title="Click to type a value" on:click|stopPropagation={start}>{shown}{suffix}</button>
{/if}

<style>
  .value {
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.58);
    text-align: right;
    font-variant-numeric: tabular-nums;
    min-width: 0;
    width: 100%;
    box-sizing: border-box;
    padding: 1px 2px;
    margin: 0;
    border: 1px solid transparent;
    border-radius: 3px;
    background: transparent;
    cursor: text;
  }
  button.value:hover { border-color: var(--grid); color: var(--text); }
  .value.edit {
    border-color: var(--accent);
    background: var(--bg);
    color: var(--text);
    outline: none;
  }
</style>
