<script>
  import { createEventDispatcher } from 'svelte'
  import { clickOutside } from './actions.js'
  import ColourPickerPopup from './ColourPickerPopup.svelte'
  import BgPalette from './BgPalette.svelte'
  const dispatch = createEventDispatcher()

  export let x = 0
  export let y = 0
  export let bg      = null     // '#rrggbbaa' | null — null = theme default
  export let pattern = 'none'   // 'none' | 'dots' | 'grid' | 'checker' | 'diagonal' | 'crosshatch' | 'rings' — see panePatterns.js
  export let patternScale   = 1  // 0.5–3, multiplies/spaces the preset's tiling
  export let patternOpacity = 5  // 0–20 (%), the pattern lines/dots' alpha — capped at 20 since
                                  // beyond that the patterns' hard-edged gradient stops (see
                                  // panePatterns.js) read as visibly pixelated/aliased rather than a
                                  // soft texture, and busy enough to fight row text legibility

  // Seed colour for the FIRST click into Custom — the app's own accent hue
  // (same value TabBar's tab-colouring already defaults to), not a grey.
  // Custom doesn't do anything until this click commits it, so there's no
  // "preserve the existing state" reason to seed with something desaturated
  // — a vivid, decently-lit start means the H/S/L tracks are all legible
  // immediately and the pane visibly changes the moment you pick Custom,
  // rather than requiring a drag first to see anything happen.
  const CUSTOM_SEED = '#74a2ffff'

  const PATTERN_OPTIONS = [
    { key: 'none',       label: 'None' },
    { key: 'dots',       label: 'Dots' },
    { key: 'grid',       label: 'Grid' },
    { key: 'checker',    label: 'Checker' },
    { key: 'diagonal',   label: 'Diagonal' },
    { key: 'crosshatch', label: 'Cross-hatch' },
    { key: 'rings',      label: 'Rings' },
  ]

  // ── nested colour popup (background swatch) — same trigger-position
  // pattern as ColourPickerRow's own swatch. MUST be rendered as a DOM
  // descendant of .popup below (not a sibling) — clickOutside checks DOM
  // containment, not visual layout, and both popups are position:fixed, so a
  // sibling would visually sit "inside" this popup while actually failing
  // the containment check: every click inside it would then read as an
  // outside click and close this whole popup before the colour change could
  // even dispatch.
  let colourPopup = null   // { x, y } | null

  function openColourPopup(rect) {
    const w = 216, h = 300
    // First entry into Custom (bg still null) commits the seed right away —
    // clicking Custom IS choosing it, not just previewing it, so the swatch
    // shows active and the pane updates before any slider's been touched.
    if (!bg) dispatch('change', { kind: 'bg', value: CUSTOM_SEED })
    colourPopup = {
      x: Math.min(rect.left, window.innerWidth  - w - 8),
      y: Math.min(rect.bottom + 4, window.innerHeight - h - 8),
    }
  }
  function onColourChange(e) {
    dispatch('change', { kind: 'bg', value: e.detail })
  }
  function resetBg() {
    dispatch('change', { kind: 'bg', value: null })
  }
  function pickPattern(key) {
    dispatch('change', { kind: 'pattern', value: key })
  }

  // ── Scale/Opacity sliders — same drag mechanics as ColourPickerPopup's
  // H/S/L channels (own copy, not imported — small enough that sharing it
  // isn't worth a cross-file dependency for two callers). ─────────────────
  function channelMove(e, min, max, setter) {
    const rect = e.currentTarget.getBoundingClientRect()
    const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width))
    setter(min + (max - min) * pct)
  }
  function onChannelDown(e, min, max, setter) {
    e.currentTarget.setPointerCapture(e.pointerId)
    channelMove(e, min, max, setter)
  }
  function onChannelMove(e, min, max, setter) {
    if (e.buttons === 1) channelMove(e, min, max, setter)
  }
  function setScale(v)   { dispatch('change', { kind: 'patternScale',   value: Math.round(v * 20) / 20 }) }
  function setOpacity(v) { dispatch('change', { kind: 'patternOpacity', value: Math.round(v) }) }
</script>

<!-- svelte-ignore a11y-click-events-have-key-events -->
<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- Same guard as TextSettingsPopup/ColourPickerPopup — without it a click
     on empty space in here bubbles past this popup into its host's own
     click handler. clickOutside listens on window in the capture phase
     (actions.js), so stopping the bubble phase here doesn't affect it. -->
<div class="popup" on:click|stopPropagation use:clickOutside={{ onClose: () => dispatch('close') }} style="left: {x}px; top: {y}px">
  <div class="section-label">Background</div>
  <BgPalette value={bg}
    on:pick={e   => dispatch('change', { kind: 'bg', value: e.detail })}
    on:reset={resetBg}
    on:custom={e => openColourPopup(e.detail)} />

  <div class="section-label">Pattern</div>
  <div class="list">
    {#each PATTERN_OPTIONS as opt (opt.key)}
      <button class="item" class:active={pattern === opt.key} class:full={opt.key === 'none'} on:click={() => pickPattern(opt.key)}>{opt.label}</button>
    {/each}
  </div>

  {#if pattern !== 'none'}
    <div class="channel">
      <span class="label">Scale</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0.5} aria-valuemax={3} aria-valuenow={patternScale}
          on:pointerdown={e => onChannelDown(e, 0.5, 3, setScale)}
          on:pointermove={e => onChannelMove(e, 0.5, 3, setScale)}
      ><div class="thumb" style="left: {(patternScale - 0.5) / (3 - 0.5) * 100}%"></div></div>
      <span class="value">{patternScale.toFixed(2)}x</span>
    </div>
    <div class="channel">
      <span class="label">Opacity</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={20} aria-valuenow={patternOpacity}
          on:pointerdown={e => onChannelDown(e, 0, 20, setOpacity)}
          on:pointermove={e => onChannelMove(e, 0, 20, setOpacity)}
      ><div class="thumb" style="left: {Math.min(100, patternOpacity / 20 * 100)}%"></div></div>
      <span class="value">{patternOpacity}%</span>
    </div>
  {/if}

  {#if colourPopup}
    <ColourPickerPopup x={colourPopup.x} y={colourPopup.y} hex={bg ?? CUSTOM_SEED}
      on:change={onColourChange}
      on:close={() => colourPopup = null} />
  {/if}
</div>

<style>
  .popup {
    position: fixed;
    width: 176px;
    background: var(--panel-bg);
    border: 1px solid var(--grid);
    border-radius: 6px;
    padding: 8px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
    z-index: 1000;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }

  .section-label {
    padding: 2px 4px 0;
    font-size: 9px;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    color: rgba(var(--text-rgb), 0.4);
  }

  .list {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 1px;
    border-top: 1px solid var(--edge-tint);
    border-bottom: 1px solid var(--edge-tint);
    padding: 4px;
  }
  .item {
    text-align: left;
    padding: 5px 6px;
    white-space: nowrap;
    border: none;
    border-radius: 4px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.75);
    font-size: 11px;
    font-family: inherit;
    cursor: pointer;
  }
  .item.full   { grid-column: 1 / -1; }   /* None spans both columns, the six patterns split below it */
  .item:hover  { background: var(--grid); color: var(--text); }
  .item.active { background: rgba(var(--accent-rgb), 0.25); color: var(--text); }

  /* Scale/Opacity sliders — same shape as ColourPickerPopup's H/S/L channels */
  .channel {
    display: grid;
    grid-template-columns: 38px 1fr 32px;
    align-items: center;
    gap: 6px;
    padding: 0 4px;
  }
  .label {
    font-size: 10px;
    font-weight: 600;
    color: rgba(var(--text-rgb), 0.43);
  }
  .value {
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.58);
    text-align: right;
    font-variant-numeric: tabular-nums;
  }
  .track {
    position: relative;
    height: 14px;
    border-radius: 7px;
    cursor: ew-resize;
    border: 1px solid rgba(0, 0, 0, 0.25);
    background: var(--grid);
  }
  .thumb {
    position: absolute;
    top: 50%;
    transform: translate(-50%, -50%);
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: #fff;
    border: 1.5px solid rgba(0, 0, 0, 0.4);
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.5);
    pointer-events: none;
  }
</style>
