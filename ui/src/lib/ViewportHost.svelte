<script>
  // Placeholder for a native Rhino viewport (SlateViewports.cs). The native
  // control sits ON TOP of the WebView and covers the whole pane, tab bar
  // included; this component only reports where the pane is and which parts
  // of it must stay clickable (cut out of the viewport's shape), and hosts the
  // viewport's two menu chips. Nothing in the HTML can paint above a native
  // window, so the viewport is hidden while a drag, menu or popup is open.
  import { onMount, onDestroy, createEventDispatcher } from 'svelte'
  import { postToCs } from './ipc.js'
  import { tabDrag, itemDrag } from '../stores/dragState.js'
  import ContextMenu from './ContextMenu.svelte'

  export let id
  export let state = null   // viewport_state saved on the tab (camera + settings)

  const dispatch = createEventDispatcher()

  let el
  let raf = null
  let last = ''
  let modes = []            // [{ id, name }] from C#
  let menu = null           // { x, y, items } | null
  // Picture of the viewport shown while the native control is hidden (C#
  // grabs it just before hiding — see BeginSnapshotHide), so the model never
  // vanishes behind a menu/popup/drag. { src, left, top, w, h } host-local.
  let snapshot = null
  let lastRect = null       // pane inner rect from the latest tick
  let clearTimer = null

  // Anything that has to draw over the viewport. Popups/menus are
  // position:fixed elements rendered inside panes; checked by class.
  const OVERLAY_SELECTOR = '.menu, .popup'
  // Bits of the pane that must stay visible and clickable over the viewport.
  const CUT_SELECTOR = '.corner, header .tab, header button, header .bar, [data-vp-cut]'

  // Pane inner rect (inside its 1px border) and the cut-outs, viewport-local.
  function measure() {
    const pane = el.closest('.pane')
    if (!pane) return null
    const p = pane.getBoundingClientRect()
    const r = { left: p.left + 1, top: p.top + 1, right: p.right - 1, bottom: p.bottom - 1 }
    const cuts = []
    for (const c of pane.querySelectorAll(CUT_SELECTOR)) {
      const h = c.getBoundingClientRect()
      const x0 = Math.max(h.left, r.left),   y0 = Math.max(h.top, r.top)
      const x1 = Math.min(h.right, r.right), y1 = Math.min(h.bottom, r.bottom)
      if (x1 > x0 && y1 > y0) cuts.push({ x: x0 - r.left, y: y0 - r.top, w: x1 - x0, h: y1 - y0 })
    }
    return { r, cuts }
  }

  // Polled every frame rather than ResizeObserver: a pane can MOVE without
  // resizing (a split elsewhere), and RO doesn't see moves. Posts only on change.
  function tick() {
    const m = el && measure()
    if (m) {
      const { r, cuts } = m
      lastRect = r
      const w = r.right - r.left, h = r.bottom - r.top
      const blocked = $tabDrag !== null || $itemDrag !== null || document.querySelector(OVERLAY_SELECTOR) !== null
      const visible = !blocked && w > 1 && h > 1
      const dpr = window.devicePixelRatio
      const key = `${r.left},${r.top},${w},${h},${dpr},${visible},${JSON.stringify(cuts)}`
      if (key !== last) {
        last = key
        // Back to visible: drop the picture once the native control is surely
        // on top again (it'd otherwise show stale through the cut-outs).
        clearTimeout(clearTimer)
        if (visible && snapshot) clearTimer = setTimeout(() => snapshot = null, 150)
        // state only matters the first time (C# creates the control from it).
        postToCs({ type: 'viewport_rect', id, x: r.left, y: r.top, w, h, dpr, visible, cuts, state })
      }
    }
    raf = requestAnimationFrame(tick)
  }

  function onMessage(e) {
    let m
    try { m = JSON.parse(e.data) } catch { return }
    if (m.id !== id) return
    if (m.type === 'viewport_info') {
      modes = m.modes ?? []
      dispatch('bg', m.bg)
    } else if (m.type === 'viewport_snapshot') {
      const host = el.getBoundingClientRect()
      const r = lastRect ?? { left: host.left, top: host.top, right: host.right, bottom: host.bottom }
      clearTimeout(clearTimer)
      snapshot = { src: m.data, left: r.left - host.left, top: r.top - host.top, w: r.right - r.left, h: r.bottom - r.top }
      // Tell C# to hide only once the picture is actually painted.
      const img = new Image()
      img.onload = img.onerror = () => requestAnimationFrame(() => postToCs({ type: 'viewport_snapshot_shown', id }))
      img.src = m.data
    } else if (m.type === 'viewport_state') {
      dispatch('state', m.state)
    }
  }

  function cmd(name, value) { postToCs({ type: 'viewport_cmd', id, cmd: name, value }) }
  const tick_ = on => on ? '✓ ' : '   '

  const VIEWS = ['Perspective', 'Two-Point', 'Top', 'Bottom', 'Front', 'Back', 'Left', 'Right']

  // Chips toggle: a second click on the same chip closes its menu. The menu
  // already closes on that click's pointerdown (clickOutside), so the click
  // itself must not reopen it — remembered here for a moment.
  let closedKind = null, closedAt = 0
  function closeMenu() {
    if (menu) { closedKind = menu.kind; closedAt = performance.now() }
    menu = null
  }
  function justClosed(kind) { return closedKind === kind && performance.now() - closedAt < 400 }

  function openMenu(e, kind, items) {
    if (justClosed(kind)) { closedKind = null; return }
    const r = e.currentTarget.getBoundingClientRect()
    const menuW = 190, menuH = items.length * 24
    menu = {
      x: Math.min(r.left, window.innerWidth - menuW - 8),
      y: Math.max(8, Math.min(r.bottom + 2, window.innerHeight - menuH - 8)),
      items,
      kind,
    }
  }

  function viewMenu(e) {
    const s = state ?? {}
    openMenu(e, 'view', [
      ...VIEWS.map(v => ({ label: tick_(s.view === v) + v, action: () => cmd('view', v) })),
      'sep',
      { label: tick_(!s.parallel) + 'Perspective Projection', action: () => cmd('projection', 'perspective') },
      { label: tick_(!!s.parallel) + 'Parallel Projection',   action: () => cmd('projection', 'parallel') },
      'sep',
      { label: '   Zoom Extents', action: () => cmd('zoomExtents') },
      { label: '   Zoom to Focus', action: () => cmd('zoomFocus') },
      'sep',
      { label: tick_(!!s.locked) + 'Locked', action: () => cmd('locked', !s.locked) },
    ])
  }

  function displayMenu(e) {
    const s = state ?? {}
    openMenu(e, 'display', [
      ...modes.map(m => ({ label: tick_(s.displayMode === m.id) + m.name, action: () => cmd('displayMode', m.id) })),
      'sep',
      { label: tick_(s.grid !== false)       + 'Grid',        action: () => cmd('grid', s.grid === false) },
      { label: tick_(s.cplaneAxes !== false) + 'CPlane Axes', action: () => cmd('cplaneAxes', s.cplaneAxes === false) },
      { label: tick_(s.worldAxes !== false)  + 'World Axes',  action: () => cmd('worldAxes', s.worldAxes === false) },
    ])
  }

  onMount(() => {
    window.chrome?.webview?.addEventListener('message', onMessage)
    raf = requestAnimationFrame(tick)
  })
  onDestroy(() => {
    clearTimeout(clearTimer)
    window.chrome?.webview?.removeEventListener('message', onMessage)
    if (raf !== null) cancelAnimationFrame(raf)
    postToCs({ type: 'viewport_remove', id })
  })
</script>

<div class="viewport-host" bind:this={el}>
  {#if snapshot}
    <img class="snapshot" src={snapshot.src} alt=""
      style="left: {snapshot.left}px; top: {snapshot.top}px; width: {snapshot.w}px; height: {snapshot.h}px" />
  {/if}
  <div class="chips">
    <button class="chip" class:open={menu?.kind === 'view'} data-vp-cut on:click|stopPropagation={viewMenu}>
      {state?.view ?? 'Perspective'}{state?.locked ? ' 🔒' : ''} ▾
    </button>
    <button class="chip" class:open={menu?.kind === 'display'} data-vp-cut on:click|stopPropagation={displayMenu}>
      {state?.displayName ?? 'Shaded'} ▾
    </button>
  </div>
</div>

{#if menu}
  <ContextMenu x={menu.x} y={menu.y} items={menu.items} on:close={closeMenu} />
{/if}

<style>
  .viewport-host {
    position: absolute;
    inset: 0;
    overflow: hidden;   /* the picture covers the whole pane; only the content part shows, the header keeps its live tabs */
  }
  .snapshot {
    position: absolute;
    pointer-events: none;
    max-width: none;
  }
  .chips {
    position: absolute;
    top: 4px;
    left: 6px;
    display: flex;
    gap: 4px;
  }
  .chip {
    height: 16px;
    padding: 0 6px;
    border: none;
    border-radius: 3px;
    /* Opaque, from the theme — a translucent fill read differently over
       every viewport background (the pane is painted the display mode's
       background, so switching to Rendered recoloured both chips). */
    background: color-mix(in srgb, var(--text) 8%, var(--bg));
    color: color-mix(in srgb, var(--text) 75%, var(--bg));
    outline: none;
    font-size: 11px;
    line-height: 16px;
    cursor: pointer;
    white-space: nowrap;
  }
  /* Two states only: rest, and hover — which also stays on while the chip's
     menu is open. No separate pressed/focus look. */
  .chip:hover, .chip:active, .chip.open {
    background: color-mix(in srgb, var(--text) 16%, var(--bg));
    color: var(--text);
  }
</style>
