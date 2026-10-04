<script>
  // Collapsible section header shared by the Path Mapper row ("Rules" and
  // "Graph") and the fullscreen rule strip, so they can't drift apart. Fixed
  // 22px (half a slider) — the row's total height is built on that pitch, see
  // PathMapperPanel.svelte.
  export let label = ''
  export let open = true
  export let note = ''        // small muted text on the right
</script>

<button class="sec-head" on:click>
  <svg class="chev" class:open width="8" height="8" viewBox="0 0 8 8" aria-hidden="true">
    <path d="M2 1l4 3-4 3z" fill="currentColor" />
  </svg>
  <span class="label">{label}</span>
  {#if note}<span class="note">{note}</span>{/if}
</button>

<style>
  .sec-head {
    display: flex;
    align-items: center;
    gap: 7px;
    width: 100%;
    height: 22px;
    padding: 0 12px;
    border: none;
    background: transparent;
    font: inherit;
    font-size: 10px;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: rgba(var(--text-rgb), 0.5);
    cursor: pointer;
    text-align: left;
    transition: color 0.12s;
  }
  .sec-head:hover { color: rgba(var(--text-rgb), 0.85); }
  .sec-head:focus-visible { outline: 1px solid var(--accent); outline-offset: -1px; border-radius: 3px; }

  .chev { flex-shrink: 0; transition: transform 0.12s ease-out; }
  .chev.open { transform: rotate(90deg); }

  .note {
    margin-left: auto;
    font-weight: 500;
    letter-spacing: 0;
    text-transform: none;
    color: rgba(var(--text-rgb), 0.4);
  }
</style>
