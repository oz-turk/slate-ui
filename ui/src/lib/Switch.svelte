<script>
  // Labelled on/off switch: same look as the toggles in the settings panel.
  // A plain checkbox kept invisible in place (focus/keyboard/click still work)
  // with a track + knob drawn on top and animated off :checked.
  export let checked = false
  export let label = ''
  export let title = ''
</script>

<label class="switch-row" {title}>
  <span class="text">{label}<slot /></span>
  <span class="switch">
    <input type="checkbox" bind:checked on:change />
    <span class="switch-track"></span>
  </span>
</label>

<style>
  .switch-row {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    cursor: pointer;
    user-select: none;
  }
  .text {
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.73);
  }
  .switch-row:hover .text { color: var(--text); }

  .switch {
    position: relative;
    display: inline-block;
    width: 32px;
    height: 18px;
    flex-shrink: 0;
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
</style>
