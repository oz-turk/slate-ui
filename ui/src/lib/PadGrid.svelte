<script>
  // Hover-only guide grid + ruler for the XY pads (MdSliderRow, GraphMapperRow).
  // Invisible until `show` is set (pointer over the pad, or a drag in progress).
  // Pure overlay — never takes pointer events.
  //
  // Ruler = a standard 10-division ruler: a tick at every tenth, a longer one at
  // the middle, the longest at both ends. Numbers are written only at min / mid /
  // max, OUTSIDE the pad (in the padbox's left/bottom padding).
  export let show = false
  export let xMin = 0
  export let xMax = 1
  export let yMin = 0
  export let yMax = 1

  const DIVISIONS = 10
  const IDX = Array.from({ length: DIVISIONS + 1 }, (_, i) => i)
  // tick length as a fraction of the ruler band: ends / middle / the rest
  const tickLen = i => (i === 0 || i === DIVISIONS ? 1 : i === DIVISIONS / 2 ? 0.7 : 0.4)

  function fmt(v) {
    const a = Math.abs(v)
    const s = a >= 100 ? v.toFixed(0) : a >= 10 ? v.toFixed(1) : v.toFixed(2)
    return s.includes('.') ? s.replace(/\.?0+$/, '') : s
  }
</script>

<div class="padgrid" class:show>
  <!-- guide lines inside the pad, one per tenth; the middle one a bit stronger -->
  <svg class="lines" viewBox="0 0 10 10" preserveAspectRatio="none">
    {#each IDX.slice(1, -1) as i}
      <line x1={i} y1="0" x2={i} y2="10" class:mid={i === DIVISIONS / 2}/>
      <line x1="0" y1={i} x2="10" y2={i} class:mid={i === DIVISIONS / 2}/>
    {/each}
  </svg>

  <!-- x ruler, below the pad -->
  <svg class="ruler rx" viewBox="0 0 10 1" preserveAspectRatio="none">
    {#each IDX as i}
      <line x1={i} y1="0" x2={i} y2={tickLen(i)}/>
    {/each}
  </svg>
  <!-- y ruler, left of the pad (i = 0 is the top, i.e. yMax) -->
  <svg class="ruler ry" viewBox="0 0 1 10" preserveAspectRatio="none">
    {#each IDX as i}
      <line x1={1 - tickLen(i)} y1={i} x2="1" y2={i}/>
    {/each}
  </svg>

  <span class="tick x x0">{fmt(xMin)}</span>
  <span class="tick x x1">{fmt((xMin + xMax) / 2)}</span>
  <span class="tick x x2">{fmt(xMax)}</span>

  <span class="tick y y0">{fmt(yMax)}</span>
  <span class="tick y y1">{fmt((yMin + yMax) / 2)}</span>
  <span class="tick y y2">{fmt(yMin)}</span>
</div>

<style>
  /* The pad itself sits inside the same box with padding: 0 0 18px 32px (see the
     .padbox rule in MdSliderRow / GraphMapperRow) — .padgrid covers exactly that
     pad area; the ruler and its numbers are placed OUTSIDE it, in the padding. */
  .padgrid {
    position: absolute;
    inset: 0 0 18px 32px;
    opacity: 0;
    transition: opacity 0.12s;
    pointer-events: none;
  }
  .padgrid.show { opacity: 1; }

  svg { position: absolute; }
  .lines { inset: 0; width: 100%; height: 100%; overflow: hidden; }
  .lines line {
    stroke: rgba(var(--text-rgb), 0.07);
    stroke-width: 1;
    vector-effect: non-scaling-stroke;
  }
  .lines line.mid { stroke: rgba(var(--text-rgb), 0.2); }

  .ruler { overflow: visible; }
  .ruler line {
    stroke: rgba(var(--text-rgb), 0.45);
    stroke-width: 1;
    vector-effect: non-scaling-stroke;
  }
  .rx { top: 100%; left: 0; width: 100%; height: 5px; }
  .ry { right: 100%; top: 0; width: 5px; height: 100%; }

  .tick {
    position: absolute;
    font-family: 'Segoe UI Mono', Consolas, monospace;
    font-size: 8px;
    line-height: 10px;
    color: rgba(var(--text-rgb), 0.5);
    white-space: nowrap;
  }
  /* x numbers: under the ruler ticks */
  .x   { top: 100%; margin-top: 6px; }
  .x0  { left: 0; }
  .x1  { left: 50%; transform: translateX(-50%); }
  .x2  { right: 0; }
  /* y numbers: left of the ruler ticks, right-aligned */
  .y   { right: 100%; margin-right: 7px; text-align: right; }
  .y0  { top: 0; }
  .y1  { top: 50%; transform: translateY(-50%); }
  .y2  { bottom: 0; }
</style>
