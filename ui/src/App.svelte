<script>
  import { onMount }    from 'svelte'
  import { get }        from 'svelte/store'
  import PaneLayout     from './lib/PaneLayout.svelte'
  import EditToolbar    from './lib/EditToolbar.svelte'
  import WorkspaceTabs  from './lib/WorkspaceTabs.svelte'
  import StatusBar      from './lib/StatusBar.svelte'
  import SettingsPanel  from './lib/SettingsPanel.svelte'
  import ParamViewerFullscreen from './lib/ParamViewerFullscreen.svelte'
  import {
    layout, workspaces, activeWorkspaceId, allLeaves, restoreLayout, restoreWorkspaces,
    updatePane, syncControl, clearAllWorkspaces, resetToDefault, makeLeaf, setActiveWorkspace,
    posAppend, applyWindowEdgeResize, orderedItems, MIN_PANE_SIZE
  } from './stores/layout.js'
  import { mode, pinned, theme, deleteRequest, captureRequest, settingsOpen, clearSelectionTick, groupSelectionTick, hoverHint, altHeld, ctrlHeld, fullscreenTreeId, paramViewerItemsCache } from './stores/uiState.js'
  import { undo, suppressDuring } from './stores/history.js'
  import { postToCs, postStateSnapshot } from './lib/ipc.js'

  // Driven from the theme store directly rather than var(--panel-bg) — that
  // var kept resolving to something dark in light mode for this specific
  // consumer across several rebuilds (root cause never pinned down; the same
  // JS-driven approach for Pane.svelte's edge-tint border was confirmed
  // working via a lime/magenta diagnostic build, so applying it here too).
  $: toolbarBg = $theme === 'light' ? '#f7f5f0' : '#303030'

  // app.css's light-mode override lives on :root[data-theme="light"] (not
  // main[data-theme] — see app.css's comment: custom properties don't
  // inherit upward from <main> to <body>/<html>), so <html> itself needs
  // the attribute too, not just the <main data-theme> below.
  $: document.documentElement.dataset.theme = $theme

  // ── C# ↔ JS ───────────────────────────────────────────────────────────────────
  onMount(() => {
    window.chrome?.webview?.addEventListener('message', e => {
      try { handleMessage(JSON.parse(e.data)) } catch {}
    })
    postToCs({ type: 'ui_ready' })
    // A document with its own saved ui_state gets this from the restore_state
    // handler below once C# restores it — but a document with none (new/
    // unsaved file, no Slate history yet) never triggers that path, so C#'s
    // native-side theme default file (see SlateWindow's WriteThemeDefaultFile)
    // would otherwise only get populated by incidental later interaction
    // (resize, a settings change...). Reporting the theme we're actually
    // opened with here, unconditionally, closes that gap on every load.
    postStateSnapshot()
  })

  // ── window-edge resize ───────────────────────────────────────────────────────
  // Which OS window edge is being dragged is detected entirely client-side:
  // a left/top-edge drag moves the window (screenX/screenY shifts alongside
  // the size change), a right/bottom-edge drag only changes size. Diffing
  // against the previous tick's screenX/screenY gives exactly the delta
  // applyWindowEdgeResize (→ shrinkTopLeftEdge) expects. This runs
  // synchronously in the same reflow pass the browser already does for the
  // resize, so there's no cross-process round trip and no flicker — unlike
  // an earlier C#-driven version of this feature that posted the delta over
  // WebView2's IPC, which was a tick behind the native reflow and visibly
  // flashed the wrong pane size on every tick before correcting.
  let prevScreenX = window.screenX
  let prevScreenY = window.screenY
  let resizeSettleTimer = null

  // A real mouse drag on a window edge fires many resize ticks, each only a
  // few px (however fast the pointer moves between browser paint frames) —
  // well under this. Aero Snap (also Win+Arrow, maximize/restore, a
  // monitor/DPI change) instead relocates+resizes the window in one native
  // jump, hundreds of px at once. shrinkTopLeftEdge below subtracts
  // deltaX/deltaY as raw px from whichever pane sits on that edge, so
  // treating a jump this size as a drag tick yanked that pane straight
  // toward MIN_PANE_SIZE in one shot (confirmed live 2026-09-21 snapping the
  // window to a screen's left edge — see pencere-snap-tab-boyutu.md). Above
  // this threshold, treat it as an OS-driven jump instead and skip the pane
  // math entirely rather than misapplying it.
  const EDGE_DRAG_MAX_TICK_PX = 100

  function onWindowResize() {
    const deltaX = window.screenX - prevScreenX
    const deltaY = window.screenY - prevScreenY
    prevScreenX = window.screenX
    prevScreenY = window.screenY
    if (Math.abs(deltaX) <= EDGE_DRAG_MAX_TICK_PX && Math.abs(deltaY) <= EDGE_DRAG_MAX_TICK_PX)
      applyWindowEdgeResize(deltaX, deltaY)

    // No native "drag ended" event to hook (unlike C#'s ResizeEnd), so commit
    // to C#/.gh persistence after a short quiet period instead — mirrors
    // history.js's own settle debounce for undo grouping.
    clearTimeout(resizeSettleTimer)
    resizeSettleTimer = setTimeout(postStateSnapshot, 400)
  }

  // Dragging the window by its title bar (a pure move, no size change) never
  // fires a `resize` event, so prevScreenX/Y would otherwise go stale after
  // any such move. The next real edge-resize tick would then diff against
  // that stale pre-move position and compute a phantom delta, shrinking a
  // pane on the opposite edge from the one actually being dragged. Polling
  // at a low frequency keeps prevScreenX/Y caught up during idle periods;
  // it's skipped while resizeSettleTimer is set (mid-gesture or still
  // within its settle window) so it never fights the real resize math.
  let moveResyncTimer = null

  onMount(() => {
    window.addEventListener('resize', onWindowResize)
    moveResyncTimer = setInterval(() => {
      if (resizeSettleTimer) return
      prevScreenX = window.screenX
      prevScreenY = window.screenY
    }, 250)
    return () => {
      window.removeEventListener('resize', onWindowResize)
      clearInterval(moveResyncTimer)
    }
  })

  // ── Global keyboard shortcuts ────────────────────────────────────────────────
  // Ignored while typing in a text field (renaming a tab/group, editing a value)
  // so keys like x/c/Tab still behave normally there instead of being hijacked.
  function isTextEditable(el) {
    return el?.tagName === 'INPUT' || el?.tagName === 'TEXTAREA' || el?.isContentEditable
  }

  // Tracked continuously so x/c can resolve "whatever's under the mouse" without
  // needing a real pointer event at keypress time.
  let mouseX = 0, mouseY = 0
  onMount(() => {
    function onPointerMove(e) { mouseX = e.clientX; mouseY = e.clientY }
    window.addEventListener('pointermove', onPointerMove)
    return () => window.removeEventListener('pointermove', onPointerMove)
  })

  // x/c resolution logic, factored out so it can fire from two sources: the
  // normal DOM keydown below, and the C# host's capture_hotkey/delete_hotkey
  // messages (see handleMessage) — a fallback for when Rhino's own command
  // line has silently stolen keyboard focus from the WebView2, which stops
  // the DOM keydown from ever firing (a general Rhino/Eto focus-routing issue
  // seen elsewhere in Rhino too, not specific to this plugin). Both triggering
  // the same already-selected GH objects is harmless — capture/delete are
  // idempotent per id (see addCapturedControl's allSliderIds() guard and
  // removeSlider's plain array filter).
  function triggerDelete() {
    // Guards against the poll-triggered path too, not just the DOM keydown
    // below — typing "x" into one of Slate's own rename boxes shouldn't
    // delete whatever's under the mouse just because the poll doesn't know
    // a text field has focus.
    if (get(mode) !== 'edit' || isTextEditable(document.activeElement)) return
    const el = document.elementFromPoint(mouseX, mouseY)
    const paneEl = el?.closest('[data-pane-id]')
    if (!paneEl) return
    const sliderEl = el?.closest('[data-slider-id]')
    if (sliderEl) { deleteRequest.set({ paneId: paneEl.dataset.paneId, sliderId: sliderEl.dataset.sliderId }); return }
    // Mouse is over a group but not over one of its slider rows specifically
    // (e.g. the header, or empty padding) — same target resolution as
    // triggerCapture's groupEl below, so "x" ungroups whatever group is
    // under the cursor, matching GroupSection's own "×" button.
    const groupEl = el?.closest('[data-group-id]')
    if (groupEl) deleteRequest.set({ paneId: paneEl.dataset.paneId, groupId: groupEl.dataset.groupId })
  }
  function triggerCapture() {
    if (get(mode) !== 'edit' || isTextEditable(document.activeElement)) return
    const el = document.elementFromPoint(mouseX, mouseY)
    const paneEl  = el?.closest('[data-pane-id]')
    // closest() walks up from whatever's directly under the mouse — a
    // slider row inside a group still resolves to that group's own .group
    // div, and a nested group resolves to the innermost one, same as the
    // group's own "+ Capture" button already does per-group.
    const groupEl = el?.closest('[data-group-id]')
    if (paneEl) captureRequest.set({ paneId: paneEl.dataset.paneId, groupId: groupEl?.dataset.groupId ?? null })
  }

  // altHeld drives the faint highlight on corner-handles (see
  // CornerHandle.svelte) so the user sees what an Alt+corner drag would
  // grab before they start dragging — only the 4 corners light up, not the
  // whole edge, so it's clear a drag has to start right at one of them.
  // ctrlHeld just feeds StatusBar's modifier-specific hint list (no visual
  // highlight elsewhere) — tracked in the same block since it needs the
  // same resync/blur handling.
  // blur is needed too — Alt+Tabbing away from the window doesn't fire a
  // keyup here.
  onMount(() => {
    function onKeydown(e) {
      if (e.key === 'Alt') altHeld.set(true)
      else if (e.key === 'Control' || e.key === 'Meta') ctrlHeld.set(true)
    }
    function onKeyup(e) {
      if (e.key === 'Alt') altHeld.set(false)
      else if (e.key === 'Control' || e.key === 'Meta') ctrlHeld.set(false)
    }
    function onBlur() { altHeld.set(false); ctrlHeld.set(false) }
    // Alt is a Windows "system key" (WM_SYSKEYUP) — mid corner-drag its
    // release can get swallowed before reaching this window, leaving
    // altHeld stuck true (corners stay hidden until a later Alt press/
    // release cycle happens to land cleanly — the "every other time"
    // symptom). Any pointer move carries the live modifier state, so
    // resync from it as a self-healing fallback instead of trusting
    // keyup alone. Gated on an actual change so it doesn't force a
    // getBoundingClientRect() in every CornerHandle on every mousemove.
    // Ctrl doesn't have that system-key quirk, but resyncing it here too
    // is free and keeps it equally self-healing.
    function onPointerMove(e) {
      if (get(altHeld) !== e.altKey) altHeld.set(e.altKey)
      const ctrl = e.ctrlKey || e.metaKey
      if (get(ctrlHeld) !== ctrl) ctrlHeld.set(ctrl)
    }
    window.addEventListener('keydown', onKeydown)
    window.addEventListener('keyup', onKeyup)
    window.addEventListener('blur', onBlur)
    window.addEventListener('pointermove', onPointerMove)
    return () => {
      window.removeEventListener('keydown', onKeydown)
      window.removeEventListener('keyup', onKeyup)
      window.removeEventListener('blur', onBlur)
      window.removeEventListener('pointermove', onPointerMove)
    }
  })

  onMount(() => {
    function onKeydown(e) {
      if (isTextEditable(document.activeElement)) return

      if (e.key === 'Tab') {
        e.preventDefault()
        mode.update(m => m === 'edit' ? 'preview' : 'edit')
        return
      }

      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        e.preventDefault()
        undo()
        return
      }

      if ((e.ctrlKey || e.metaKey) && e.key >= '1' && e.key <= '9') {
        const idx = parseInt(e.key, 10) - 1
        const ws = get(workspaces)[idx]
        if (ws) { e.preventDefault(); setActiveWorkspace(ws.id); postStateSnapshot() }
        return
      }

      if (get(mode) !== 'edit') return

      if (e.key === 'Escape') {
        clearSelectionTick.update(n => n + 1)
        return
      }

      if (e.key === 'x') { triggerDelete(); return }
      if (e.key === 'c') { triggerCapture(); return }
      if (e.key === 'g') { groupSelectionTick.update(n => n + 1); return }
    }
    window.addEventListener('keydown', onKeydown)
    return () => window.removeEventListener('keydown', onKeydown)
  })

  // Spans every workspace, not just the active one — a GH object already
  // captured somewhere shouldn't silently get captured again into another.
  function allSliderIds() {
    const ids = new Set()
    function collectGroups(groups) {
      for (const g of groups) {
        g.sliders.forEach(s => ids.add(s.id))
        collectGroups(g.groups ?? [])
      }
    }
    for (const w of get(workspaces))
      for (const leaf of allLeaves(w.layout))
        for (const t of leaf.tabs) {
          t.sliders.forEach(s => ids.add(s.id))
          collectGroups(t.groups)
        }
    return ids
  }

  function addSliderToGroupInTree(groups, groupId, slider) {
    return groups.map(g =>
      g.id === groupId
        ? { ...g, sliders: [...g.sliders, { ...slider, pos: posAppend(g) }] }
        : { ...g, groups: addSliderToGroupInTree(g.groups ?? [], groupId, slider) }
    )
  }

  // Shared by every "captured a control" message (slider_added, toggle_added, ...) —
  // finds where it should land (by tabId/label, falling back to the first pane/tab)
  // and inserts it into that tab's top-level list or the target group.
  function addCapturedControl(control, msg) {
    if (allSliderIds().has(control.id)) return
    const $l  = get(layout)
    const leaves = allLeaves($l)
    let targetPaneId = null, targetTabId = null
    for (const leaf of leaves) {
      const t = leaf.tabs.find(t => t.id === msg.tabId)
             ?? leaf.tabs.find(t => t.label.toLowerCase() === (msg.tabId ?? '').toLowerCase())
      if (t) { targetPaneId = leaf.paneId; targetTabId = t.id; break }
    }
    if (!targetPaneId) {
      // Same stale-activeTabId guard as Pane.svelte's activeTab derivation —
      // a leaf's stored activeTabId can point at a tab that's no longer
      // there, and inserting into that id here would silently drop the
      // captured control (nothing in p.tabs would match it).
      const fallbackLeaf = leaves[0]
      targetPaneId = fallbackLeaf?.paneId
      targetTabId  = fallbackLeaf?.tabs.find(t => t.id === fallbackLeaf.activeTabId)?.id ?? fallbackLeaf?.tabs[0]?.id
    }
    if (!targetPaneId) return
    const groupId = msg.groupId ?? null
    updatePane(targetPaneId, p => ({
      tabs: p.tabs.map(t => t.id !== targetTabId ? t : groupId
        ? { ...t, groups: addSliderToGroupInTree(t.groups, groupId, control) }
        : { ...t, sliders: [...t.sliders, { ...control, pos: posAppend(t) }] })
    }))
    postStateSnapshot()
  }

  // ── sort_positions_result support ────────────────────────────────────────────
  function collectSliderIdsDeep(sliders, groups) {
    const ids = (sliders ?? []).map(s => s.id)
    for (const g of groups ?? []) ids.push(...collectSliderIdsDeep(g.sliders, g.groups))
    return ids
  }
  // [y, x] sort key — a slider uses its own live pivot; a group uses the
  // topmost (then leftmost) pivot among its slider descendants, at any
  // nesting depth, so a group sorts to wherever its "highest" child sits.
  // Missing/unresolved positions (id no longer live in the GH doc, or an
  // empty group) sort last.
  function sortPositionKey(item, positions) {
    if (item.kind === 'slider') {
      const p = positions[item.id]
      return p ? [p[1], p[0]] : [Infinity, Infinity]
    }
    let best = [Infinity, Infinity]
    for (const id of collectSliderIdsDeep(item.data.sliders, item.data.groups)) {
      const p = positions[id]
      if (!p) continue
      if (p[1] < best[0] || (p[1] === best[0] && p[0] < best[1])) best = [p[1], p[0]]
    }
    return best
  }
  // Unlike reorderTabTopLevel (used by the pane's Type/Name sorts, which
  // deliberately move a group as one untouched block), Canvas Position is
  // expected to reach inside groups too — the same recursive position lookup
  // above (a group's key = its topmost/leftmost descendant) already implies
  // "wherever things actually sit on the canvas", so leaving a group's own
  // contents in whatever order they happened to be captured in would read as
  // only half-sorted. Recurses into every level of `container` (tab or
  // group), re-ranking that level's own sliders+groups by position and
  // handing each subgroup back through itself for its own contents.
  function sortContainerByPosition(container, positions) {
    const ranked = [...orderedItems(container)].sort((a, b) => {
      const ka = sortPositionKey(a, positions), kb = sortPositionKey(b, positions)
      return ka[0] - kb[0] || ka[1] - kb[1]
    })
    const rank = new Map(ranked.map((it, i) => [it.id, i]))
    return {
      ...container,
      sliders: container.sliders.map(s => ({ ...s, pos: rank.get(s.id) })),
      groups: (container.groups ?? []).map(g =>
        sortContainerByPosition({ ...g, pos: rank.get(g.id) }, positions)
      ),
    }
  }

  // WebView2's own Ctrl+scroll zoom — C# posts the new factor as it changes;
  // shown as a transient status-bar hint (same slot hover hints use) since
  // there's no hover/leave pair to key off of, just cleared a beat after the
  // last event so a burst of wheel ticks doesn't flicker it.
  let zoomHideTimer = null
  function showZoomHint(factor) {
    hoverHint.set(`Zoom: ${Math.round(factor * 100)}%`)
    clearTimeout(zoomHideTimer)
    zoomHideTimer = setTimeout(() => hoverHint.set(null), 900)
  }

  function handleMessage(msg) {
    if (msg.type === 'zoom_changed') {
      showZoomHint(msg.factor)
    }

    // Ground truth from the C# host's GetAsyncKeyState poll (see
    // SlateWindow.cs) — supersedes the DOM keydown/keyup/pointermove
    // tracking below whenever it arrives, since it isn't subject to
    // Chromium's own Alt/system-key handling swallowing the event.
    if (msg.type === 'alt_state') {
      altHeld.set(msg.held)
    }

    // Same GetAsyncKeyState poll, watching 'c'/'x' instead of Alt — see
    // triggerCapture/triggerDelete above and the poll timer in SlateWindow.cs.
    if (msg.type === 'capture_hotkey') triggerCapture()
    if (msg.type === 'delete_hotkey')  triggerDelete()
    // Same poll again for 'g' — no mouse-position resolution needed (it acts
    // on whatever's already selected, not whatever's under the cursor), but
    // still needs the ground-truth fallback since GH's canvas has its own
    // native "Group" command on the same key that a stolen keydown would hit.
    if (msg.type === 'group_hotkey') groupSelectionTick.update(n => n + 1)

    if (msg.type === 'slider_added') {
      addCapturedControl({ id: msg.id, type: 'slider', name: msg.name, min: msg.min, max: msg.max, value: msg.value, decimalPlaces: msg.decimalPlaces }, msg)
    }

    if (msg.type === 'toggle_added') {
      addCapturedControl({ id: msg.id, type: 'toggle', name: msg.name, value: msg.value }, msg)
    }

    if (msg.type === 'button_added') {
      addCapturedControl({ id: msg.id, type: 'button', name: msg.name, value: msg.value }, msg)
    }

    if (msg.type === 'valueList_added') {
      addCapturedControl({ id: msg.id, type: 'valueList', name: msg.name, options: msg.options, value: msg.value, multiSelect: msg.multiSelect, cycle: msg.cycle, loop: msg.loop }, msg)
    }

    if (msg.type === 'panel_added') {
      addCapturedControl({ id: msg.id, type: 'panel', name: msg.name, value: msg.value, readOnly: msg.readOnly }, msg)
    }

    if (msg.type === 'itemPicker_added') {
      addCapturedControl({ id: msg.id, type: 'itemPicker', name: msg.name, options: msg.options, value: msg.value }, msg)
    }

    // Human plugin's "Item Selector" — same shape as valueList/itemPicker on the
    // wire, kept as its own type so it round-trips through RestoreState correctly.
    if (msg.type === 'humanValueList_added') {
      addCapturedControl({ id: msg.id, type: 'humanValueList', name: msg.name, options: msg.options, value: msg.value, multiSelect: msg.multiSelect, cycle: msg.cycle, loop: msg.loop }, msg)
    }

    if (msg.type === 'colourPicker_added') {
      addCapturedControl({ id: msg.id, type: 'colourPicker', name: msg.name, value: msg.value }, msg)
    }

    // Pancake plugin's "True Only Button" — same shape as a core button on the
    // wire, kept as its own type so it round-trips through RestoreState correctly.
    if (msg.type === 'pancakeButton_added') {
      addCapturedControl({ id: msg.id, type: 'pancakeButton', name: msg.name, value: msg.value }, msg)
    }

    // Native "Trigger" (GH_Timer) — no single "value": interval (signed ms,
    // sign = mode) + intervalString (GH's own formatted text) + lockTargets.
    if (msg.type === 'trigger_added') {
      addCapturedControl({ id: msg.id, type: 'trigger', name: msg.name, interval: msg.interval, intervalString: msg.intervalString, lockTargets: msg.lockTargets }, msg)
    }

    // Native geometry-holding param (Point/Curve/Brep/Mesh/Surface/SubD/Box/
    // generic Geometry). internalize defaults false here (native GH parity —
    // see SlateEvent.GeometryParamAdded) and is never touched by C# again;
    // only "count" changes after a pick (geometryParam_updated below).
    if (msg.type === 'geometryParam_added') {
      addCapturedControl({ id: msg.id, type: 'geometryParam', name: msg.name, geomKind: msg.geomKind, count: msg.count, wired: msg.wired, internalize: false }, msg)
    }

    if (msg.type === 'geometryParam_updated') {
      syncControl(msg.id, { name: msg.name, count: msg.count, wired: msg.wired })
    }

    // Native "Data Dam" (GH_DataDamComponent) — mode is "always"/"never"/
    // "delay" (mirrors the native right-click submenu's own combined preset
    // list, see yapilacaklar/data-dam-capture.md); delayLabel is C#'s own
    // formatted text (no native display-string property exists here, unlike
    // Trigger's intervalString). gateLinked/gateName start false/null — see
    // dataDam_gate_selected below for how a Select Gate link is attached.
    if (msg.type === 'dataDam_added') {
      addCapturedControl({ id: msg.id, type: 'dataDam', name: msg.name, mode: msg.mode, delaySeconds: msg.delaySeconds, delayLabel: msg.delayLabel, transferPossible: msg.transferPossible, gateLinked: msg.gateLinked, gateName: msg.gateName, gateMode: 'once' }, msg)
    }

    // Catches Mode/Delay/name changes made directly on the native canvas,
    // plus a gate link breaking (its source deleted on the canvas) — see
    // PushDataDamUpdates/PushDataDamGateUpdates in SlateWindow.cs. Never
    // touches gateId (see dataDam_gate_selected for the only thing that does).
    if (msg.type === 'dataDam_update') {
      syncControl(msg.id, { name: msg.name, mode: msg.mode, delaySeconds: msg.delaySeconds, delayLabel: msg.delayLabel, transferPossible: msg.transferPossible, gateLinked: msg.gateLinked, gateName: msg.gateName })
    }

    // One-shot reply to DataDamRow's Select Gate button. gateId has no live
    // GH counterpart (pure Slate bookkeeping, like geometryParam's
    // "internalize") — RestoreState can't refresh it from anywhere, so it's
    // persisted explicitly here rather than relying on the periodic
    // dataDam_update push above (which never carries or triggers a save).
    if (msg.type === 'dataDam_gate_selected') {
      syncControl(msg.id, { gateLinked: msg.gateLinked, gateName: msg.gateName, gateId: msg.gateId })
      postStateSnapshot()
    }

    // Native "Param Viewer" (GH_ParamViewer) — tree is the generic
    // { kids: [...] } / { count } shape built from GH's own GraphicTree
    // (see BuildParamViewerTree in SlateWindow.cs). Re-pushed on every solve
    // (paramViewer_update) since the wired source's data can change freely,
    // same reasoning as panel_text_update.
    if (msg.type === 'paramViewer_added') {
      addCapturedControl({ id: msg.id, type: 'paramViewer', name: msg.name, tree: msg.tree }, msg)
    }

    if (msg.type === 'paramViewer_update') {
      syncControl(msg.id, { name: msg.name, tree: msg.tree })
    }

    if (msg.type === 'slider_name_update') {
      syncControl(msg.id, { name: msg.name })
    }

    // Panels, item pickers and Item Selectors can change on their own between
    // solves (panel: upstream data flowing into a connected/read-only one; the
    // other two: their wired candidate list) — synced separately since they
    // carry more than a name.
    if (msg.type === 'panel_text_update') {
      syncControl(msg.id, { name: msg.name, value: msg.text, readOnly: msg.readOnly })
    }

    if (msg.type === 'itemPicker_update') {
      syncControl(msg.id, { name: msg.name, options: msg.options, value: msg.value })
    }

    if (msg.type === 'humanValueList_update') {
      syncControl(msg.id, { name: msg.name, options: msg.options, value: msg.value, multiSelect: msg.multiSelect, cycle: msg.cycle, loop: msg.loop })
    }

    // Native Value List's items are hand-edited on the canvas, not wired —
    // but still need the same live sync once they've already been added to
    // a Slate tab (see PushValueListUpdates in SlateWindow.cs).
    if (msg.type === 'valueList_update') {
      syncControl(msg.id, { name: msg.name, options: msg.options, value: msg.value, multiSelect: msg.multiSelect, cycle: msg.cycle, loop: msg.loop })
    }

    if (msg.type === 'colourPicker_update') {
      syncControl(msg.id, { name: msg.name, value: msg.value })
    }

    // Catches Interval/LockTargets changes made directly on the native canvas
    // (ModeBox/LockBox, or the right-click Interval submenu) — see
    // PushTriggerUpdates in SlateWindow.cs.
    if (msg.type === 'trigger_update') {
      syncControl(msg.id, { name: msg.name, interval: msg.interval, intervalString: msg.intervalString, lockTargets: msg.lockTargets })
    }

    if (msg.type === 'cleared') {
      clearAllWorkspaces()
      postStateSnapshot()
    }

    if (msg.type === 'reset') {
      resetToDefault()
      postStateSnapshot()
    }

    // Reply to Pane.svelte's 'sort_positions_request' (its right-click Sort:
    // Canvas Position) — msg.positions is { sliderId: [x, y] }, the live GH
    // pivot for each slider id it asked about. Fetched fresh and used once
    // here to compute a new order for that tab, recursively through every
    // group (see sortContainerByPosition); the coordinates themselves are
    // never stored, only the resulting `pos` — same field every other
    // reorder already persists.
    if (msg.type === 'sort_positions_result') {
      const leaf = allLeaves(get(layout)).find(l => l.tabs.some(t => t.id === msg.tabId))
      const tab = leaf?.tabs.find(t => t.id === msg.tabId)
      if (leaf && tab) {
        const sortedTab = sortContainerByPosition(tab, msg.positions)
        updatePane(leaf.paneId, p => ({
          tabs: p.tabs.map(t => t.id === tab.id ? sortedTab : t)
        }))
        postStateSnapshot()
      }
    }

    // Reply to DataTreeSunburst's 'paramViewer_request_items' — see
    // GetParamViewerLeafItems in SlateWindow.cs. Cached by key so a leaf the
    // user has already drilled into (and hasn't re-solved since) doesn't
    // re-request on every back/forward through it.
    if (msg.type === 'paramViewer_items_result') {
      const key = `${msg.id}:${(msg.path ?? []).join(',')}`
      paramViewerItemsCache.update(c => ({ ...c, [key]: msg.items }))
    }

    if (msg.type === 'restore_state') {
      // msg.workspaces is the current multi-workspace format
      // msg.layout is the older single-tree format
      // msg.tabs is the oldest, legacy flat format
      // Loading a file is not a user action — suppress it so it doesn't land
      // on the undo stack (undoing past it would blow away the loaded layout).
      const winW = msg.winW ?? 900, winH = msg.winH ?? 600
      // savedWinW/savedWinH (from C#) is the window size the layout was LAST
      // SAVED against, before this open's screen-fit clamp (see
      // SlateWindow.ApplyGeometry) possibly shrank it for a smaller screen —
      // e.g. a layout authored on a 5K monitor, reopened on a 4K one. When
      // that differs from the actual (winW/winH) size, every existing sizeA
      // is rescaled per axis so panes keep their proportions instead of the
      // trailing ~20% landing off-screen. A ~1 ratio (same-resolution reopen,
      // the common case) is left alone rather than nudged by rounding.
      const rawScaleX = msg.savedWinW ? winW / msg.savedWinW : 1
      const rawScaleY = msg.savedWinH ? winH / msg.savedWinH : 1
      const scaleX = Math.abs(rawScaleX - 1) < 0.01 ? 1 : rawScaleX
      const scaleY = Math.abs(rawScaleY - 1) < 0.01 ? 1 : rawScaleY
      suppressDuring(() => {
        if (msg.workspaces) {
          restoreWorkspaces(
            msg.workspaces.map(w => ({ ...w, layout: reconcileLayout(w.layout, winW, winH, scaleX, scaleY) })),
            msg.activeWorkspaceId
          )
        } else if (msg.layout) {
          restoreLayout(reconcileLayout(msg.layout, winW, winH, scaleX, scaleY))
        } else if (msg.tabs) {
          // legacy: single pane
          const leaf = makeLeaf(msg.tabs, msg.activeTabId)
          restoreLayout(leaf)
        }
      })
      // Same fallback as readDefaultTheme (uiState.js) / ReadThemeDefaultFile
      // (SlateWindow.cs) — an old file saved under the since-removed 'beta'
      // theme (2026-09-22..09-24) lands on 'dark' instead of an unstyled value.
      if (msg.theme) theme.set(msg.theme === 'light' ? 'light' : 'dark')
      postStateSnapshot()
    }
  }

  // Walk a restored layout tree and hydrate it (re-attach live slider refs).
  // The tree already has values/min/max from C# RestoreState.
  //
  // Also migrates pre-sizeA saves: those split nodes only have a fractional
  // "ratio" (0-1), not a pixel sizeA — converted here using (w, h), the pixel
  // space actually available to this node. That starts as the whole window
  // (winW/winH, sent alongside restore_state — the window size the ratio was
  // set against) and shrinks going down the tree as ancestor splits carve it
  // up, so each split's ratio is read against the space it really had.
  //
  // scaleX/scaleY (default 1, i.e. no-op): applied to an already-pixel sizeA
  // instead of the legacy ratio conversion above — see the restore_state
  // handler's comment on savedWinW/savedWinH for why this is separate from
  // the ratio migration path (different screen/resolution than last save,
  // not an old file format).
  function reconcileLayout(node, w, h, scaleX = 1, scaleY = 1) {
    if (node.type === 'leaf') return node
    let sizeA = node.sizeA != null
      ? Math.max(MIN_PANE_SIZE, Math.round(node.sizeA * (node.dir === 'h' ? scaleX : scaleY)))
      : (node.ratio != null ? Math.round(node.ratio * (node.dir === 'h' ? w : h)) : 260)
    const aw = node.dir === 'h' ? sizeA : w
    const ah = node.dir === 'h' ? h : sizeA
    const bw = node.dir === 'h' ? Math.max(0, w - sizeA) : w
    const bh = node.dir === 'h' ? h : Math.max(0, h - sizeA)
    return { ...node, sizeA, a: reconcileLayout(node.a, aw, ah, scaleX, scaleY), b: reconcileLayout(node.b, bw, bh, scaleX, scaleY) }
  }
</script>

<main class:edit={$mode === 'edit'} data-theme={$theme}>
  <header class="global-toolbar" style="background: {toolbarBg}">
    <WorkspaceTabs mode={$mode} />
    <EditToolbar
      bind:mode={$mode}
      bind:pinned={$pinned}
      on:pin={e => postToCs({ type: 'pin', value: e.detail })}
    />
  </header>
  {#if $settingsOpen}<SettingsPanel />{/if}

  <div class="layout-root">
    <PaneLayout node={$layout} />
  </div>

  <StatusBar />
</main>

{#if $fullscreenTreeId}<ParamViewerFullscreen />{/if}

<style>
  :global(*, *::before, *::after) { box-sizing: border-box; margin: 0; padding: 0; }
  :global(body) {
    background: var(--bg);
    color: var(--text);
    font-family: 'Segoe UI', system-ui, sans-serif;
    font-size: 13px;
    overflow: hidden;
    user-select: none;
    -webkit-font-smoothing: antialiased;
  }

  main {
    display: flex;
    flex-direction: column;
    height: 100vh;
    position: relative;
  }
  main.edit { outline: 1px solid rgba(var(--accent-rgb), 0.2); }

  .global-toolbar {
    flex-shrink: 0;
    display: flex;
    align-items: stretch;
    /* background set inline from toolbarBg (JS/$theme-driven, see above) */
    border-radius: 0 0 4px 4px;
    overflow: hidden;
    margin-bottom: 3px;
  }

  .layout-root {
    flex: 1;
    overflow: hidden;
    display: flex;
    padding: 0 3px 3px 3px;
  }
</style>
