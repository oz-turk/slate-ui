<script>
  // Appearance-only popup for Text Panel rows (PanelRow.svelte) — alignment,
  // colour, font size. Works the same whether the panel is showing as a
  // standalone `#`/`##`/`###` header or as a normal boxed text panel; text
  // itself is still typed inline in the row (this popup never touches it),
  // so there's no duplicate editor to keep in sync.
  import { createEventDispatcher } from 'svelte'
  import { clickOutside } from './actions.js'
  import ColourPickerPopup from './ColourPickerPopup.svelte'
  import BgPalette from './BgPalette.svelte'
  const dispatch = createEventDispatcher()

  export let x = 0
  export let y = 0
  export let align       = 'left'  // 'left' | 'center' | 'right'
  export let color       = null    // '#RRGGBBAA' | null — null = theme default
  export let fontSize    = null    // number (px) | null — null = theme default
  export let defaultSize = 11      // the size that applies when fontSize is null (11 for a normal panel, 15/13/12 for header levels 1/2/3)
  export let bold        = false
  export let italic      = false
  export let underline   = false

  // Optional "Background" section on top (group headers use it; Text Panel
  // rows don't) — shared BgPalette, see its header.
  export let showBg  = false
  export let bgColor = null    // '#RRGGBBAA' | null

  // ── nested colour popup — same trigger-position pattern as PaneSettingsPopup's
  // own bg swatch. MUST be a DOM descendant of .popup below (not a sibling) —
  // clickOutside checks DOM containment, and both popups are position:fixed,
  // so a sibling would visually sit "inside" this popup while actually
  // failing the containment check, closing this whole popup on every click
  // into the nested one before the colour change could dispatch.
  let colourPopup = null   // { x, y, target: 'text' | 'bg' } | null

  $: swatchStyle = `background-image: linear-gradient(${color ?? 'transparent'}, ${color ?? 'transparent'}),
      linear-gradient(45deg, #4a4a4a 25%, transparent 25%),
      linear-gradient(-45deg, #4a4a4a 25%, transparent 25%),
      linear-gradient(45deg, transparent 75%, #4a4a4a 75%),
      linear-gradient(-45deg, transparent 75%, #4a4a4a 75%);
    background-size: 100% 100%, 8px 8px, 8px 8px, 8px 8px, 8px 8px;
    background-position: 0 0, 0 0, 0 4px, 4px -4px, -4px 0;
    background-color: #2a2a2a;`

  function openColourPopup(rect, target = 'text') {
    const w = 216, h = 300
    colourPopup = {
      x: Math.min(rect.left, window.innerWidth  - w - 8),
      y: Math.min(rect.bottom + 4, window.innerHeight - h - 8),
      target,
    }
  }
  function onColourChange(e) { dispatch(colourPopup?.target === 'bg' ? 'bgChange' : 'colorChange', e.detail) }
  function resetColor()      { dispatch('colorChange', null) }

  function pickAlign(a) { dispatch('alignChange', a) }

  function toggleBold()      { dispatch('boldChange', !bold) }
  function toggleItalic()    { dispatch('italicChange', !italic) }
  function toggleUnderline() { dispatch('underlineChange', !underline) }

  // Slider drag mechanics — own copy, not imported from PaneSettingsPopup
  // (same reasoning it gave for not sharing with ColourPickerPopup: small
  // enough that a cross-file dependency for two callers isn't worth it).
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
  const FONT_MIN = 9, FONT_MAX = 28
  $: displaySize = fontSize ?? defaultSize
  function setFontSize(v) { dispatch('fontSizeChange', Math.round(v)) }
  function resetFontSize() { dispatch('fontSizeChange', null) }
</script>

<!-- svelte-ignore a11y-click-events-have-key-events -->
<!-- svelte-ignore a11y-no-static-element-interactions -->
<!-- This popup is a DOM descendant of PanelRow's .row (position:fixed only
     changes where it's drawn, not where it lives in the tree) — without this,
     a click on anything in here (align/colour/style/size controls) bubbles
     up through .text-wrap into .row's own on:click, which toggles the row's
     selection on every interaction. clickOutside listens on window in the
     capture phase (see actions.js), which already fires before this, so
     stopping the bubble phase here doesn't affect it. -->
<div class="popup" on:click|stopPropagation use:clickOutside={{ onClose: () => dispatch('close') }} style="left: {x}px; top: {y}px">
  {#if showBg}
    <div class="section-label">Background</div>
    <BgPalette value={bgColor}
      on:pick={e   => dispatch('bgChange', e.detail)}
      on:reset={() => dispatch('bgChange', null)}
      on:custom={e => openColourPopup(e.detail, 'bg')} />
  {/if}

  <div class="section-label">Alignment</div>
  <div class="align-row">
    <button class:active={align === 'left'} on:click={() => pickAlign('left')} title="Align left">
      <svg width="12" height="10" viewBox="0 0 12 10" fill="currentColor"><rect width="12" height="1.4"/><rect y="4.3" width="8" height="1.4"/><rect y="8.6" width="10" height="1.4"/></svg>
    </button>
    <button class:active={align === 'center'} on:click={() => pickAlign('center')} title="Align center">
      <svg width="12" height="10" viewBox="0 0 12 10" fill="currentColor"><rect width="12" height="1.4"/><rect x="2" y="4.3" width="8" height="1.4"/><rect x="1" y="8.6" width="10" height="1.4"/></svg>
    </button>
    <button class:active={align === 'right'} on:click={() => pickAlign('right')} title="Align right">
      <svg width="12" height="10" viewBox="0 0 12 10" fill="currentColor"><rect width="12" height="1.4"/><rect x="4" y="4.3" width="8" height="1.4"/><rect x="2" y="8.6" width="10" height="1.4"/></svg>
    </button>
  </div>

  <div class="section-label">Style</div>
  <div class="align-row">
    <button class:active={bold} on:click={toggleBold} title="Bold"><strong>B</strong></button>
    <button class:active={italic} on:click={toggleItalic} title="Italic"><em>I</em></button>
    <button class:active={underline} on:click={toggleUnderline} title="Underline"><span class="u">U</span></button>
  </div>

  <div class="section-label">Colour</div>
  <div class="bg-row">
    <button class="swatch" class:active={!color} on:click={resetColor} title="Theme default">
      <span class="swatch-reset">×</span>
    </button>
    <button class="swatch" class:active={!!color} style={swatchStyle} on:click={e => openColourPopup(e.currentTarget.getBoundingClientRect())} title="Custom colour"></button>
  </div>

  <div class="section-label">Font size</div>
  <div class="channel">
    <div class="track" role="slider" tabindex="0" aria-valuemin={FONT_MIN} aria-valuemax={FONT_MAX} aria-valuenow={displaySize}
        on:pointerdown={e => onChannelDown(e, FONT_MIN, FONT_MAX, setFontSize)}
        on:pointermove={e => onChannelMove(e, FONT_MIN, FONT_MAX, setFontSize)}
    ><div class="thumb" style="left: {(displaySize - FONT_MIN) / (FONT_MAX - FONT_MIN) * 100}%"></div></div>
    <span class="value">{displaySize}px</span>
    {#if fontSize !== null}
      <button class="reset-btn" on:click={resetFontSize} title="Theme default">×</button>
    {/if}
  </div>

  {#if colourPopup}
    <ColourPickerPopup x={colourPopup.x} y={colourPopup.y}
      hex={colourPopup.target === 'bg' ? (bgColor ?? '#8caaee47') : (color ?? '#e8e8e8ff')}
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

  .align-row, .bg-row {
    display: flex;
    gap: 4px;
    padding: 0 4px;
  }
  .align-row button {
    flex: 1;
    height: 20px;
    padding: 0;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .align-row button:hover  { color: rgba(var(--text-rgb), 0.75); border-color: var(--border); }
  .align-row button.active { color: var(--accent-light); border-color: rgba(var(--accent-rgb), 0.5); }
  .align-row button { font-size: 11px; font-family: inherit; }
  .align-row .u { text-decoration: underline; }

  .swatch {
    width: 24px;
    height: 24px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    color: rgba(var(--text-rgb), 0.5);
    padding: 0;
    overflow: hidden;
  }
  .swatch.active   { border-color: rgba(var(--accent-rgb), 0.6); }
  .swatch-reset    { font-size: 12px; line-height: 1; }

  .channel {
    display: grid;
    grid-template-columns: 1fr 32px auto;
    align-items: center;
    gap: 6px;
    padding: 0 4px;
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
  .reset-btn {
    width: 16px;
    height: 16px;
    padding: 0;
    border: none;
    background: transparent;
    color: rgba(var(--text-rgb), 0.4);
    font-size: 12px;
    line-height: 1;
    cursor: pointer;
    border-radius: 3px;
  }
  .reset-btn:hover { background: var(--grid); color: rgba(var(--text-rgb), 0.75); }
</style>
