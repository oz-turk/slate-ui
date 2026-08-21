<script>
  import { createEventDispatcher, onMount } from 'svelte'
  import { hex8ToRgba, rgbaToHex8, rgbToHsl, hslToRgb } from './colorUtils.js'
  const dispatch = createEventDispatcher()

  export let x   = 0
  export let y   = 0
  export let hex = '#ffffffff'   // #RRGGBBAA

  let panelEl
  let tab = 'hsla'   // 'hsla' | 'rgba' — HSLA opens first

  $: ({ r, g, b, a } = hex8ToRgba(hex))
  $: ({ h, s, l }    = rgbToHsl(r, g, b))
  $: alphaPct = Math.round(a / 255 * 100)
  $: hexRgb   = hex.slice(1, 7)

  function emit(rgba) {
    hex = rgbaToHex8(rgba)
    dispatch('change', hex)
  }

  function setRgb(patch)   { emit({ r, g, b, a, ...patch }) }
  function setHsl(patch) {
    const nh = patch.h ?? h, ns = patch.s ?? s, nl = patch.l ?? l
    const rgb = hslToRgb(nh, ns, nl)
    emit({ r: rgb.r, g: rgb.g, b: rgb.b, a: patch.a ?? a })
  }
  function setAlpha(pct) {
    const na = Math.round(Math.max(0, Math.min(100, pct)) / 100 * 255)
    emit({ r, g, b, a: na })
  }

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

  function onHexInput(e) {
    const v = e.target.value.replace(/[^0-9a-fA-F]/g, '').slice(0, 6)
    if (v.length === 6) emit({ ...hex8ToRgba('#' + v), a })
  }

  function onDocPointerDown(e) {
    if (panelEl && !panelEl.contains(e.target)) dispatch('close')
  }
  function onKeydown(e) {
    if (e.key === 'Escape') dispatch('close')
  }
  onMount(() => {
    window.addEventListener('pointerdown', onDocPointerDown, true)
    window.addEventListener('keydown', onKeydown)
    return () => {
      window.removeEventListener('pointerdown', onDocPointerDown, true)
      window.removeEventListener('keydown', onKeydown)
    }
  })

  // gradient backgrounds — each track's own visual IS the value indicator
  $: hueBg  = `linear-gradient(to right, #f00, #ff0, #0f0, #0ff, #00f, #f0f, #f00)`
  $: satBg  = `linear-gradient(to right, hsl(${h}, 0%, ${l}%), hsl(${h}, 100%, ${l}%))`
  $: lightBg = `linear-gradient(to right, #000, hsl(${h}, ${s}%, 50%), #fff)`
  $: rBg = `linear-gradient(to right, rgb(0,${g},${b}), rgb(255,${g},${b}))`
  $: gBg = `linear-gradient(to right, rgb(${r},0,${b}), rgb(${r},255,${b}))`
  $: bBg = `linear-gradient(to right, rgb(${r},${g},0), rgb(${r},${g},255))`
  $: alphaBg = `linear-gradient(to right, rgba(${r},${g},${b},0), rgba(${r},${g},${b},1)),
                 repeating-conic-gradient(#5a5a5a 0% 25%, #3a3a3a 0% 50%) 50% / 8px 8px`
</script>

<div class="popup" bind:this={panelEl} style="left: {x}px; top: {y}px">
  <div class="tabs">
    <button class="tab" class:active={tab === 'hsla'} on:click={() => tab = 'hsla'}>HSLA</button>
    <button class="tab" class:active={tab === 'rgba'} on:click={() => tab = 'rgba'}>RGBA</button>
  </div>

  <div class="preview-row">
    <div class="swatch" style="background: {hex}"></div>
    <span class="hash">#</span>
    <input class="hex-input" value={hexRgb} maxlength="6" spellcheck="false" on:change={onHexInput} />
    <span class="alpha-readout">{alphaPct}%</span>
  </div>

  {#if tab === 'hsla'}
    <div class="channel">
      <span class="label">H</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={360} aria-valuenow={Math.round(h)} style="background: {hueBg}"
          on:pointerdown={e => onChannelDown(e, 0, 360, v => setHsl({ h: v }))}
          on:pointermove={e => onChannelMove(e, 0, 360, v => setHsl({ h: v }))}
      ><div class="thumb" style="left: {h / 360 * 100}%"></div></div>
      <span class="value">{Math.round(h)}°</span>
    </div>
    <div class="channel">
      <span class="label">S</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(s)} style="background: {satBg}"
          on:pointerdown={e => onChannelDown(e, 0, 100, v => setHsl({ s: v }))}
          on:pointermove={e => onChannelMove(e, 0, 100, v => setHsl({ s: v }))}
      ><div class="thumb" style="left: {s}%"></div></div>
      <span class="value">{Math.round(s)}%</span>
    </div>
    <div class="channel">
      <span class="label">L</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(l)} style="background: {lightBg}"
          on:pointerdown={e => onChannelDown(e, 0, 100, v => setHsl({ l: v }))}
          on:pointermove={e => onChannelMove(e, 0, 100, v => setHsl({ l: v }))}
      ><div class="thumb" style="left: {l}%"></div></div>
      <span class="value">{Math.round(l)}%</span>
    </div>
  {:else}
    <div class="channel">
      <span class="label">R</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={255} aria-valuenow={Math.round(r)} style="background: {rBg}"
          on:pointerdown={e => onChannelDown(e, 0, 255, v => setRgb({ r: v }))}
          on:pointermove={e => onChannelMove(e, 0, 255, v => setRgb({ r: v }))}
      ><div class="thumb" style="left: {r / 255 * 100}%"></div></div>
      <span class="value">{Math.round(r)}</span>
    </div>
    <div class="channel">
      <span class="label">G</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={255} aria-valuenow={Math.round(g)} style="background: {gBg}"
          on:pointerdown={e => onChannelDown(e, 0, 255, v => setRgb({ g: v }))}
          on:pointermove={e => onChannelMove(e, 0, 255, v => setRgb({ g: v }))}
      ><div class="thumb" style="left: {g / 255 * 100}%"></div></div>
      <span class="value">{Math.round(g)}</span>
    </div>
    <div class="channel">
      <span class="label">B</span>
      <div class="track" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={255} aria-valuenow={Math.round(b)} style="background: {bBg}"
          on:pointerdown={e => onChannelDown(e, 0, 255, v => setRgb({ b: v }))}
          on:pointermove={e => onChannelMove(e, 0, 255, v => setRgb({ b: v }))}
      ><div class="thumb" style="left: {b / 255 * 100}%"></div></div>
      <span class="value">{Math.round(b)}</span>
    </div>
  {/if}

  <div class="channel">
    <span class="label">A</span>
    <div class="track checker" role="slider" tabindex="0" aria-valuemin={0} aria-valuemax={100} aria-valuenow={alphaPct} style="background: {alphaBg}"
        on:pointerdown={e => onChannelDown(e, 0, 100, setAlpha)}
        on:pointermove={e => onChannelMove(e, 0, 100, setAlpha)}
    ><div class="thumb" style="left: {alphaPct}%"></div></div>
    <span class="value">{alphaPct}%</span>
  </div>
</div>

<style>
  .popup {
    position: fixed;
    width: 216px;
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

  .tabs {
    display: flex;
    gap: 4px;
  }
  .tab {
    flex: 1;
    height: 20px;
    border: 1px solid var(--grid);
    border-radius: 3px;
    background: transparent;
    color: rgba(var(--text-rgb), 0.58);
    font-size: 10px;
    font-weight: 600;
    letter-spacing: 0.02em;
    font-family: inherit;
    cursor: pointer;
    transition: border-color 0.15s, color 0.15s;
  }
  .tab:hover  { border-color: var(--border); color: rgba(var(--text-rgb), 0.73); }
  .tab.active { border-color: rgba(var(--accent-rgb), 0.4); color: var(--accent-light); }

  .preview-row {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .swatch {
    width: 24px;
    height: 24px;
    border-radius: 4px;
    border: 1px solid var(--grid);
    flex-shrink: 0;
    background-image:
      linear-gradient(45deg, #4a4a4a 25%, transparent 25%),
      linear-gradient(-45deg, #4a4a4a 25%, transparent 25%),
      linear-gradient(45deg, transparent 75%, #4a4a4a 75%),
      linear-gradient(-45deg, transparent 75%, #4a4a4a 75%);
    background-size: 8px 8px;
    background-position: 0 0, 0 4px, 4px -4px, -4px 0;
    background-color: #2a2a2a;
  }
  .hash {
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 11px;
    color: rgba(var(--text-rgb), 0.43);
  }
  .hex-input {
    flex: 1;
    min-width: 0;
    height: 20px;
    background: var(--bg);
    border: 1px solid var(--grid);
    border-radius: 3px;
    color: var(--text);
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 11px;
    text-transform: uppercase;
    padding: 0 6px;
  }
  .hex-input:focus { border-color: var(--accent); outline: none; }
  .alpha-readout {
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 10px;
    color: rgba(var(--text-rgb), 0.43);
    flex-shrink: 0;
    width: 30px;
    text-align: right;
  }

  .channel {
    display: grid;
    grid-template-columns: 12px 1fr 32px;
    align-items: center;
    gap: 8px;
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
  }
  .track.checker { background-color: #2a2a2a; }

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
