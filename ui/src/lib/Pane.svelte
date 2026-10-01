<script>
  import TabBar       from './TabBar.svelte'
  import SliderRow    from './SliderRow.svelte'
  import ToggleRow    from './ToggleRow.svelte'
  import ButtonRow    from './ButtonRow.svelte'
  import ValueListRow from './ValueListRow.svelte'
  import PanelRow     from './PanelRow.svelte'
  import ColourPickerRow from './ColourPickerRow.svelte'
  import TriggerRow   from './TriggerRow.svelte'
  import GeometryParamRow from './GeometryParamRow.svelte'
  import DataDamRow   from './DataDamRow.svelte'
  import ParamViewerRow from './ParamViewerRow.svelte'
  import GroupSection from './GroupSection.svelte'
  import ActionBar    from './ActionBar.svelte'
  import CornerHandle from './CornerHandle.svelte'
  import ContextMenu  from './ContextMenu.svelte'
  import PaneSettingsPopup from './PaneSettingsPopup.svelte'
  import { PANE_PATTERNS, PANE_PATTERN_DEFAULTS } from './panePatterns.js'
  import { compositeOverOpaque } from './colorUtils.js'
  import { layout, activeWorkspaceId, updatePane, findLeaf, newTabId, splitPane, splitPaneSpanning, newSplitId, setSplitSize, collapsePane, findNeighborPane, moveCrossPaneItem, extractSlidersByIds, orderedItems, posBetween, posAppend, withAppendedPositions, reorderTabTopLevel, removeAllBySourceId, countBySourceId } from '../stores/layout.js'
  import { tabDrag, itemDrag } from '../stores/dragState.js'
  import { computeAlignedSnapTargets, snapRaw } from './splitSnap.js'
  import { mode, deleteRequest, captureRequest, clearSelectionTick, groupSelectionTick, hoverHint, theme } from '../stores/uiState.js'
  import { postToCs, postStateSnapshot } from './ipc.js'
  import { showParamIcons } from '../stores/uiState.js'
  import { get } from 'svelte/store'
  import { flip } from 'svelte/animate'
  import { cubicOut } from 'svelte/easing'

  export let paneId

  // slider.type → row component (falls back to SliderRow when unset/unknown)
  // itemPicker reuses ValueListRow — same "pick one from a list" UI, the only
  // difference (candidate list from a wired input vs manually authored) lives
  // entirely on the C# side.
  const ROW_COMPONENTS = { toggle: ToggleRow, button: ButtonRow, valueList: ValueListRow, panel: PanelRow, itemPicker: ValueListRow, humanValueList: ValueListRow, colourPicker: ColourPickerRow, pancakeButton: ButtonRow, trigger: TriggerRow, geometryParam: GeometryParamRow, dataDam: DataDamRow, paramViewer: ParamViewerRow }

  // Fixed type order for the pane's manual "Sort: Type" menu item — same
  // priority SlatePanel.cs's capture loop checks types in, just reused here
  // as an explicit ranking rather than a type-check chain.
  const TYPE_ORDER = ['slider', 'toggle', 'button', 'valueList', 'panel', 'itemPicker', 'humanValueList', 'colourPicker', 'pancakeButton', 'trigger', 'geometryParam', 'dataDam']

  // ── derive pane state reactively (not derived() — paneId must stay live) ─────
  $: pane        = findLeaf($layout, paneId)
  $: tabs        = pane?.tabs        ?? []
  // activeTab falls back to tabs[0] whenever pane.activeTabId doesn't match any
  // tab still in this pane (stale id — e.g. left over from a tab move/removal
  // that didn't update this leaf). activeTabId is derived FROM that already-
  // corrected activeTab, not read straight off pane.activeTabId, so every
  // downstream use of it (TabBar's highlight, capture, cross-pane drop target)
  // agrees with what's actually being shown — otherwise TabBar could show no
  // tab as active at all while this pane still renders tabs[0]'s content, and
  // capture/cross-pane-drop would silently target a tab id that doesn't exist.
  $: activeTab   = tabs.find(t => t.id === pane?.activeTabId) ?? tabs[0]
  $: activeTabId = activeTab?.id ?? null
  $: activeItems = activeTab ? orderedItems(activeTab) : []

  // SliderRow's name column is a fixed grid width (needs to line up across
  // every row so the tracks align) — sized here per-tab from that tab's own
  // longest slider name (top-level + nested in groups), rather than one
  // constant for every tab, so "Slider1" doesn't leave a wide dead gap while
  // a longer name elsewhere still fits. Rough char-count estimate, not a
  // measured width — biased wide (not tight) since a little extra slack costs
  // nothing but an under-estimate clips into the ellipsis.
  // Trigger and GeometryParam also grid-align their name column to this
  // width (see TriggerRow.svelte / GeometryParamRow.svelte), so their names
  // count toward the measurement too — every other typed row (toggle/
  // button/...) stays right-anchored and excluded.
  function isSliderRowType(s) { return !ROW_COMPONENTS[s.type] || s.type === 'trigger' || s.type === 'geometryParam' || s.type === 'dataDam' }
  function maxSliderNameLen(sliders, groups) {
    let max = 0
    for (const s of sliders ?? []) if (isSliderRowType(s)) max = Math.max(max, (s.name ?? '').length)
    for (const g of groups ?? []) max = Math.max(max, maxSliderNameLen(g.sliders, g.groups))
    return max
  }
  // +18 leaves room for the 16px param icon (ParamIcon.svelte) inside the name column.
  $: nameColWidth = Math.min(180, Math.max(70, maxSliderNameLen(activeTab?.sliders, activeTab?.groups) * 7 + 20)) + ($showParamIcons ? 18 : 0)

  // "Show icons": ask C# once per captured object for its GH icon
  // (icons_result lands in App.svelte -> iconKeys/iconImages). Tracked in
  // requestedIcons rather than by "has a key yet", since objects with no icon
  // never get a key and would otherwise be re-asked on every store update.
  const requestedIcons = new Set()
  function collectSourceIds(sliders, groups, out) {
    for (const s of sliders ?? []) out.add(s.sourceId ?? s.id)
    for (const g of groups ?? []) collectSourceIds(g.sliders, g.groups, out)
    return out
  }
  $: if ($showParamIcons && activeTab) {
    const missing = [...collectSourceIds(activeTab.sliders, activeTab.groups, new Set())].filter(id => !requestedIcons.has(id))
    if (missing.length) {
      missing.forEach(id => requestedIcons.add(id))
      postToCs({ type: 'icons_request', ids: missing })
    }
  }

  // Driven straight from the theme store instead of the --edge-tint CSS
  // custom property — that var resolves fine for every *other* consumer
  // (--ws-tab-bg etc, same override block) but this specific border kept
  // rendering solid black in light mode across several rebuilds, so this
  // sidesteps whatever cascade issue that was rather than keep guessing at it.
  $: edgeTint   = $theme === 'light' ? 'rgba(0, 0, 0, 0.08)'   : 'rgba(255, 255, 255, 0.04)'

  // Per-pane background colour/pattern override (right-click → Pane
  // Settings). Was gated to a separate 'beta' theme while this feature was
  // developed (see yapilacaklar/gorsel-cesitlendirme.md, bölüm 8-10) — beta
  // is gone now (2026-09-24), this works on dark/light directly.
  // 'style.bg'/'style.pattern' are each optional; a missing one falls
  // through to --bg/none (no per-pane override at all — Pane Settings
  // itself defaults to 'none', not an inherited theme pattern, see
  // panePatterns.js) since only the properties present here get an inline
  // value at all.
  //
  // Set as CUSTOM PROPERTIES (read by .pane's own background-color/-image
  // rules below), not assigned as background-color/-image directly here —
  // keeps this reactive block just building a style STRING, agnostic of
  // which CSS properties end up consuming it. header used to also read
  // these vars and paint its own copy (to fix an older bug — see .pane's
  // style block comment), but that double-painted the same translucent
  // colour a second time on top of .pane's own fill; header is transparent
  // now and just shows .pane's single paint through instead.
  $: paneStyleCss = (() => {
    const s = pane?.style
    if (!s) return ''
    const parts = []
    const baseHex = $theme === 'light' ? '#e4e1d8' : '#1a1a1a'
    if (s.bg) {
      parts.push(`--pane-bg-override: ${s.bg}`)
      // Opaque variant for DataTreeSunburst's arc-stroke gap colour — text-
      // shadow/filter respect the colour's OWN alpha, so if s.bg has reduced
      // opacity the outline would be equally translucent and let whatever's
      // behind bleed through right where it's meant to mask it. This used to
      // just drop the alpha suffix ('#rrggbbaa' → '#rrggbb'), which at low
      // alpha comes out far more saturated than what's actually on screen (a
      // 50%-alpha red reads as a soft pink once blended, not solid red) —
      // composite s.bg over the theme's own flat base surface instead, same
      // colour the pane itself visually reads as. Single composite is
      // accurate here specifically because the pane paints its translucent
      // fill exactly once (header used to double-paint the same colour on
      // top — see this block's header comment below — that's gone now); a
      // second stacked alpha layer anywhere in this chain would need a
      // second composite pass. NOT used by row-hover/handle/label knockouts
      // any more — see --pane-pattern-mask below for why they need a
      // separate variable rather than sharing this one.
      parts.push(`--pane-bg-override-opaque: ${compositeOverOpaque(s.bg, baseHex)}`)
    }
    if (s.pattern && s.pattern !== 'none' && s.pattern !== 'theme') {
      const p = PANE_PATTERNS[s.pattern] ?? PANE_PATTERNS.none
      const scale   = s.patternScale   ?? PANE_PATTERN_DEFAULTS.scale
      const opacity = s.patternOpacity ?? PANE_PATTERN_DEFAULTS.opacity
      parts.push(`--pane-pattern-image-override: ${p.image(opacity, scale)}`, `--pane-pattern-size-override: ${p.size(scale)}`)
      // Flat colour that knocks the pattern out locally — row hover and the
      // handle/chevron/label/bound knockout outlines all fall back to
      // `transparent` (paint nothing) when this is unset, which is always
      // exactly correct with no pattern since there's nothing behind them to
      // hide. Only compute/apply it once a pattern actually exists to mask;
      // composited approximations of a saturated, low-alpha override colour
      // can visibly drift from the pane's true resting surface (rounding,
      // base-colour assumptions), which showed up as a highlight/halo on
      // every row even with no pattern present when this used to share
      // --pane-bg-override-opaque unconditionally.
      parts.push(`--pane-pattern-mask: ${s.bg ? compositeOverOpaque(s.bg, baseHex) : baseHex}`)
    }
    return parts.length ? '; ' + parts.join('; ') : ''
  })()

  function openPaneSettings(clientX, clientY) {
    // h is sized for the common case (Scale/Opacity sliders visible, since
    // sized for the common case (a pattern picked, Scale/Opacity sliders
    // visible, 7-item pattern list) — a couple px of slack under the popup
    // when None is picked instead is harmless.
    const w = 160, h = 340
    paneSettingsPopup = {
      x: Math.min(clientX, window.innerWidth  - w - 8),
      y: Math.min(clientY, window.innerHeight - h - 8),
    }
  }
  function onPaneSettingsChange(e) {
    const { kind, value } = e.detail
    updatePane(paneId, p => ({ style: { ...(p.style ?? {}), [kind]: value } }))
    postStateSnapshot()
  }

  // "x"/"c" hotkeys — App.svelte resolves what's under the mouse via the DOM
  // (data-pane-id / data-slider-id) and sets these; whichever Pane matches acts.
  $: if ($deleteRequest?.paneId === paneId && activeTab) {
    if ($deleteRequest.sliderId) removeSlider(activeTab.id, $deleteRequest.sliderId)
    else if ($deleteRequest.groupId) removeGroup($deleteRequest.groupId)
    deleteRequest.set(null)
  }
  $: if ($captureRequest?.paneId === paneId && activeTabId) {
    postToCs({ type: 'capture', tabId: activeTabId, groupId: $captureRequest.groupId ?? undefined })
    captureRequest.set(null)
  }

  // Escape — every pane clears its own selection, not just the one under the mouse.
  $: if ($clearSelectionTick) clearSelection()

  // "g" — groups whichever pane's selection is non-empty; groupSelected()
  // itself no-ops when this pane has nothing selected, same as clearSelection above.
  $: if ($groupSelectionTick) groupSelected()

  // Leaving edit mode: selection is an edit-mode concept, so the blue
  // highlight shouldn't persist once preview mode hides the ActionBar.
  $: if ($mode !== 'edit') clearSelection()

  // ── local UI state ────────────────────────────────────────────────────────────
  let selectedIds   = new Set()
  // Anchor for shift range-select — the last slider explicitly clicked
  // (plain or toggled). Reset alongside selectedIds in clearSelection so a
  // stale anchor from a different tab can never leak a cross-tab range.
  let lastClickedId = null
  let activeDrag    = null
  let dropTarget    = null
  let lastReorderKey = null  // dedupe key so hovering the same slider-row spot doesn't re-splice every event
  let reorderRaf      = null // pending rAF for the deferred live reorder (see setDropTarget)

  // ── tab drag (cross-pane) drop state ─────────────────────────────────────────
  let edgeZone     = null   // 'left' | 'right' | 'top' | 'bottom' | 'center' | null
  let itemDragHover = false  // true only while a cross-pane item drag is hovering this pane
  let paneEl
  // id of the row currently being resized, or null. While set, flip is
  // skipped for the WHOLE list (see row-slot), not just this row — every
  // resize tick reflows the rows below it, and animating that reflow at
  // flip's normal 150ms would restart on each tick faster than it can
  // finish, reading as a trailing/laggy chase instead of the rows tracking
  // the drag directly.
  let resizingSliderId = null
  let contextMenu = null  // { x, y, items } | null — right-click menu on the pane itself
  let paneSettingsPopup = null  // { x, y } | null — pane's own background colour/pattern override

  // The "Right-click: split or close" hint is only useful while hovering
  // empty pane background — over a row/group it just sits there hiding
  // whatever that control might want to say, even though right-click still
  // works there too. mouseover/mouseout (bubbling) instead of mouseenter/
  // mouseleave so re-entering the background between rows re-shows it.
  function onContentMouseOver(e) {
    if ($mode !== 'edit') return
    hoverHint.set(e.target.closest('[data-slider-id], [data-group-id]') ? null : 'Right-click: split or close this pane')
  }
  function onContentMouseOut(e) {
    if (!e.currentTarget.contains(e.relatedTarget)) hoverHint.set(null)
  }

  // Menu-triggered split (unlike drag-split, there's no cursor position to
  // derive a ratio from) — sizeA is set to half of the pane's current pixel
  // size so the fresh pane lands dead centre instead of the fixed default.
  function splitEven(dir) {
    const rect = paneEl.getBoundingClientRect()
    splitPane(paneId, dir, 'after', (dir === 'h' ? rect.width : rect.height) / 2)
    postStateSnapshot()
  }

  function onPaneContextMenu(e) {
    e.preventDefault()
    const { clientX, clientY } = e
    const menuW = 170, menuH = 240
    const items = [
      { label: 'Split Vertically',   action: () => splitEven('h') },
      { label: 'Split Horizontally', action: () => splitEven('v') },
      'sep',
      { label: 'Sort: Canvas Position', action: () => sortActiveTab('position') },
      { label: 'Sort: Type',            action: () => sortActiveTab('type') },
      { label: 'Sort: Name (A-Z)',      action: () => sortActiveTab('name') },
      'sep',
      { label: 'Pane Settings…', action: () => openPaneSettings(clientX, clientY) },
    ]
    items.push('sep', { label: 'Close Pane', danger: true, action: () => { collapsePane(paneId); postStateSnapshot() } })
    contextMenu = {
      x: Math.min(clientX, window.innerWidth  - menuW - 8),
      y: Math.min(clientY, window.innerHeight - menuH - 8),
      items
    }
  }

  // Right-click on a single slider row — "Delete" mirrors the plain "x"
  // hotkey (this widget only), "Delete All Copies" mirrors Alt+X (every
  // widget sharing this control's sourceId, any workspace — see
  // removeAllBySourceId in layout.js). The second entry only makes sense,
  // and only shows, when this control actually has more than one widget.
  function onSliderContextMenu(pos, slider) {
    const menuW = 170, menuH = 80
    const sourceId = slider.sourceId ?? slider.id
    const items = [{ label: 'Delete', action: () => removeSlider(activeTab.id, slider.id) }]
    if (countBySourceId(sourceId) > 1) {
      items.push({ label: 'Delete All Copies', danger: true, action: () => { removeAllBySourceId(sourceId); postStateSnapshot() } })
    }
    contextMenu = {
      x: Math.min(pos.x, window.innerWidth  - menuW - 8),
      y: Math.min(pos.y, window.innerHeight - menuH - 8),
      items
    }
  }

  // ── manual sort (pane right-click) ────────────────────────────────────────────
  // Reorders the active tab's top-level sliders+groups only — a group moves as
  // one block, its own contents untouched.
  function collectSliderIds(sliders, groups) {
    const ids = (sliders ?? []).map(s => s.id)
    for (const g of groups ?? []) ids.push(...collectSliderIds(g.sliders, g.groups))
    return ids
  }
  function sortKeyName(item) {
    return String((item.kind === 'group' ? item.data.label : item.data.name) ?? '').toLowerCase()
  }
  function sortKeyType(item) {
    if (item.kind === 'group') return TYPE_ORDER.length
    const idx = TYPE_ORDER.indexOf(item.data.type)
    return idx < 0 ? TYPE_ORDER.length : idx
  }
  function sortActiveTab(criterion) {
    if (!activeTab) return
    if (criterion === 'position') {
      // No canvas position is stored client-side (would bloat every saved
      // .gh file) — ask C# for the live GH pivot of each slider id instead;
      // App.svelte's 'sort_positions_result' handler finishes the reorder.
      // C# only knows GH guids, so this sends sourceId, not the widget's own
      // id — two copies of the same control share one guid and so share one
      // canvas position, which is the only sensible answer for a copy anyway.
      const ids = collectSliderIds(activeTab.sliders, activeTab.groups).map(sourceIdFor)
      if (ids.length) postToCs({ type: 'sort_positions_request', tabId: activeTab.id, ids })
      return
    }
    const keyFn = criterion === 'name' ? sortKeyName : sortKeyType
    const sortedIds = [...activeItems]
      .sort((a, b) => { const ka = keyFn(a), kb = keyFn(b); return ka < kb ? -1 : ka > kb ? 1 : 0 })
      .map(it => it.id)
    mutateTabs(tabs => tabs.map(t => t.id === activeTab.id ? reorderTabTopLevel(t, sortedIds) : t))
  }

  // ── pane mutation helpers ─────────────────────────────────────────────────────
  function mutateTabs(fn) {
    updatePane(paneId, p => {
      const tabs = fn([...p.tabs])
      return { tabs }
    })
    postStateSnapshot()
  }

  // same as mutateTabs but skips the snapshot post — used for live drag reorder,
  // which fires on every hover change; snapshot is sent once when the drag ends
  function mutateTabsQuiet(fn) {
    updatePane(paneId, p => ({ tabs: fn([...p.tabs]) }))
  }

  function setActive(id) {
    updatePane(paneId, () => ({ activeTabId: id }))
  }

  // ── group tree helpers ────────────────────────────────────────────────────────
  function mapGroupTree(groups, groupId, fn) {
    return groups.map(g =>
      g.id === groupId
        ? { ...g, ...fn(g) }
        : { ...g, groups: mapGroupTree(g.groups ?? [], groupId, fn) }
    )
  }
  function removeFromGroupTree(groups, groupId) {
    return groups
      .filter(g => g.id !== groupId)
      .map(g => ({ ...g, groups: removeFromGroupTree(g.groups ?? [], groupId) }))
  }
  function extractGroupFromTree(groups, groupId) {
    let found = null
    function walk(gs) {
      return gs.filter(g => { if (g.id === groupId) { found = g; return false } return true })
               .map(g => ({ ...g, groups: walk(g.groups ?? []) }))
    }
    return [walk(groups), found]
  }
  // Inserts a block of sliders together, next to targetId, in whichever
  // container (top-level or targetGroupId's) it lives in — order within the
  // block is kept. Visual order comes from `pos`, not array position (see
  // orderedItems in layout.js), so each item gets a fresh pos placing it
  // right next to targetId; array append order below is otherwise incidental.
  function insertSlidersAt(container, targetId, pos, items) {
    const ordered = orderedItems(container)
    const idx = ordered.findIndex(it => it.id === targetId)
    const neighborBefore = idx < 0 ? ordered[ordered.length - 1] : (pos === 'after' ? ordered[idx]     : ordered[idx - 1])
    const neighborAfter  = idx < 0 ? null                        : (pos === 'after' ? ordered[idx + 1] : ordered[idx])
    let p = neighborBefore?.pos ?? null
    const placed = items.map(it => {
      const newPos = posBetween(p, neighborAfter?.pos ?? null)
      p = newPos
      return { ...it, pos: newPos }
    })
    return { ...container, sliders: [...container.sliders, ...placed] }
  }

  // value may be a plain value or a (prevValue => newValue) updater — the
  // latter lets callers (e.g. checklist toggles) compute the new value from
  // whatever it currently is, without a separate read first.
  function updateSliderInGroups(groups, sliderId, value) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.map(s => s.id === sliderId ? { ...s, value: typeof value === 'function' ? value(s.value) : value } : s),
      groups: updateSliderInGroups(g.groups ?? [], sliderId, value)
    }))
  }
  function removeSliderFromGroups(groups, sliderId) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.filter(s => s.id !== sliderId),
      groups: removeSliderFromGroups(g.groups ?? [], sliderId)
    }))
  }

  // ── slider value update (no snapshot — too frequent) ─────────────────────────
  function updateSliderValue(sliderId, value) {
    updatePane(paneId, p => ({
      tabs: p.tabs.map(t => ({
        ...t,
        sliders: t.sliders.map(s => s.id === sliderId ? { ...s, value: typeof value === 'function' ? value(s.value) : value } : s),
        groups:  updateSliderInGroups(t.groups, sliderId, value)
      }))
    }))
  }

  // For a multiSelect control (CheckList/Sequence), `value` is the index the
  // user just clicked — toggle it into/out of the current selection locally,
  // but tell C# just that one index; ToggleItem() there does the rest.
  function changeMessageType(controlType) {
    if (controlType === 'panel') return 'panel_change'
    if (controlType === 'colourPicker') return 'colour_change'
    return 'slider_change'
  }
  function onSliderChange(sliderId, value, controlType, multiSelect) {
    if (controlType === 'trigger') {
      onTriggerChange(sliderId, value)
      return
    }
    if (controlType === 'geometryParam') {
      onGeometryParamChange(sliderId, value)
      return
    }
    if (controlType === 'dataDam') {
      onDataDamChange(sliderId, value)
      return
    }
    if (multiSelect) {
      updateSliderValue(sliderId, prev => {
        const arr = Array.isArray(prev) ? prev : []
        return arr.includes(value) ? arr.filter(i => i !== value) : [...arr, value].sort((a, b) => a - b)
      })
    } else {
      updateSliderValue(sliderId, value)
    }
    postToCs({ type: changeMessageType(controlType), id: sourceIdFor(sliderId), value })
  }

  // Trigger has no single scalar "value" — its row dispatches one of three
  // discrete actions instead (see TriggerRow.svelte's dispatch('change', ...)).
  function patchSlider(sliderId, patch) {
    updatePane(paneId, p => ({
      tabs: p.tabs.map(t => ({
        ...t,
        sliders: t.sliders.map(s => s.id === sliderId ? { ...s, ...patch } : s),
        groups:  patchSliderFieldInGroups(t.groups, sliderId, patch)
      }))
    }))
  }

  // Text Panel appearance (alignment/colour/font size) — pure UI preference,
  // same as GeometryParam's previewPinned/internalize: no postToCs, GH has
  // no concept of it, just persisted via the next snapshot. Applies to any
  // Text Panel row (standalone header look or the normal boxed one, see
  // PanelRow's textStyle), not just headers. One flat value per panel, so
  // patchSlider's plain replacement patch (already group-aware) is enough.
  function onTextAlignChange(sliderId, align) {
    patchSlider(sliderId, { textAlign: align })
    postStateSnapshot()
  }
  function onTextColorChange(sliderId, color) {
    patchSlider(sliderId, { textColor: color })
    postStateSnapshot()
  }
  function onTextFontSizeChange(sliderId, size) {
    patchSlider(sliderId, { textFontSize: size })
    postStateSnapshot()
  }
  function onTextBoldChange(sliderId, value) {
    patchSlider(sliderId, { textBold: value })
    postStateSnapshot()
  }
  function onTextItalicChange(sliderId, value) {
    patchSlider(sliderId, { textItalic: value })
    postStateSnapshot()
  }
  function onTextUnderlineChange(sliderId, value) {
    patchSlider(sliderId, { textUnderline: value })
    postStateSnapshot()
  }
  // Param Viewer preview appearance — same "pure UI preference, no postToCs"
  // reasoning as the Text Panel settings above. size: number (px, a MODULE
  // multiple) or null to fall back to the row's own auto-measured default.
  function onParamViewerSizeChange(sliderId, size) {
    patchSlider(sliderId, { paramViewerSize: size })
    postStateSnapshot()
  }
  function onParamViewerShowCountsChange(sliderId, value) {
    patchSlider(sliderId, { showCounts: value })
    postStateSnapshot()
  }
  function onTriggerChange(sliderId, detail) {
    if (detail.kind === 'fire') {
      postToCs({ type: 'trigger_fire', id: sourceIdFor(sliderId) })
      return
    }
    if (detail.kind === 'lock') {
      patchSlider(sliderId, { lockTargets: detail.value })
      postToCs({ type: 'trigger_lock_change', id: sourceIdFor(sliderId), value: detail.value })
      return
    }
    if (detail.kind === 'interval') {
      patchSlider(sliderId, { interval: detail.interval, intervalString: detail.intervalString })
      postToCs({ type: 'trigger_interval_change', id: sourceIdFor(sliderId), value: detail.interval })
    }
  }

  // GeometryParamRow dispatches either a pick request (opens Rhino's object
  // picker in C#, response comes back as geometryParam_updated) or a local
  // internalize-toggle (pure UI preference, no live GH counterpart — see
  // SlateEvent.GeometryParamAdded — so it's persisted here explicitly rather
  // than relying on the next RestoreState to refresh it from live state).
  function onGeometryParamChange(sliderId, detail) {
    if (detail.kind === 'pick') {
      postToCs({ type: 'geometry_pick', id: sourceIdFor(sliderId), internalize: !!detail.internalize })
      return
    }
    if (detail.kind === 'clear') {
      postToCs({ type: 'geometry_clear', id: sourceIdFor(sliderId) })
      return
    }
    if (detail.kind === 'bake') {
      postToCs({ type: 'geometry_bake', id: sourceIdFor(sliderId) })
      return
    }
    if (detail.kind === 'internalize') {
      patchSlider(sliderId, { internalize: detail.value })
      postStateSnapshot()
      // Turning it ON bakes whatever's already captured right away (see
      // InternalizeExistingData in SlateWindow.cs) — otherwise a user who
      // picks, then internalizes, then deletes the Rhino object still loses
      // the data, since the param would still be holding a live reference.
      // Turning it OFF is a no-op on existing data (nothing to send).
      if (detail.value) postToCs({ type: 'geometry_internalize_change', id: sourceIdFor(sliderId) })
      return
    }
    if (detail.kind === 'pin') {
      // Pure UI preference, same as internalize above — C# recomputes the
      // pinned set from the next state_snapshot (see ApplyPreviewPins).
      patchSlider(sliderId, { previewPinned: detail.value })
      postStateSnapshot()
    }
  }

  // DataDamRow dispatches one of five discrete actions, same shape as
  // TriggerRow's. "selectGate" doesn't patch anything locally — C# resolves
  // the canvas selection async and replies with dataDam_gate_selected (see
  // App.svelte), which is also the one that persists the link (gateId has no
  // live GH counterpart, same reasoning as geometryParam's "internalize").
  function onDataDamChange(sliderId, detail) {
    if (detail.kind === 'fire') {
      postToCs({ type: 'datadam_fire', id: sourceIdFor(sliderId) })
      return
    }
    if (detail.kind === 'mode') {
      patchSlider(sliderId, { mode: detail.mode, delaySeconds: detail.delaySeconds, delayLabel: detail.delayLabel })
      postToCs({ type: 'datadam_mode_change', id: sourceIdFor(sliderId), mode: detail.mode, delaySeconds: detail.delaySeconds })
      return
    }
    if (detail.kind === 'gateMode') {
      // Pure Slate bookkeeping, same reasoning as geometryParam's
      // "internalize" — no live GH counterpart, so persisted explicitly
      // rather than relying on a RestoreState refresh.
      patchSlider(sliderId, { gateMode: detail.gateMode })
      postStateSnapshot()
      postToCs({ type: 'datadam_gate_mode_change', id: sourceIdFor(sliderId), gateMode: detail.gateMode })
      return
    }
    if (detail.kind === 'selectGate') {
      postToCs({ type: 'datadam_select_gate', id: sourceIdFor(sliderId) })
      return
    }
    if (detail.kind === 'clearGate') {
      // No local patch — same reasoning as selectGate, C# replies with
      // dataDam_gate_selected (gateLinked: false) which persists the clear.
      postToCs({ type: 'datadam_clear_gate', id: sourceIdFor(sliderId) })
    }
  }
  function onSliderCommit(sliderId, value, controlType) {
    updateSliderValue(sliderId, value)
    postToCs({ type: changeMessageType(controlType), id: sourceIdFor(sliderId), value })
  }

  // ── panel resize (UI-only — no postToCs, GH doesn't know about row height) ───
  function patchSliderFieldInGroups(groups, sliderId, patch) {
    return groups.map(g => ({
      ...g,
      sliders: g.sliders.map(s => s.id === sliderId ? { ...s, ...patch } : s),
      groups: patchSliderFieldInGroups(g.groups ?? [], sliderId, patch)
    }))
  }
  function onPanelResize(sliderId, height, commit) {
    updatePane(paneId, p => ({
      tabs: p.tabs.map(t => ({
        ...t,
        sliders: t.sliders.map(s => s.id === sliderId ? { ...s, height } : s),
        groups:  patchSliderFieldInGroups(t.groups, sliderId, { height })
      }))
    }))
    if (commit) postStateSnapshot()
  }

  // ── tab management ────────────────────────────────────────────────────────────
  function addTab() {
    const id = newTabId()
    mutateTabs(tabs => [...tabs, { id, label: 'Tab ' + (tabs.length + 1), sliders: [], groups: [] }])
    updatePane(paneId, () => ({ activeTabId: id }))
  }

  function renameTab(id, label) {
    mutateTabs(tabs => tabs.map(t => t.id === id ? { ...t, label } : t))
  }

  function setTabColor(id, color) {
    mutateTabs(tabs => tabs.map(t => t.id === id ? { ...t, color } : t))
  }

  function removeTab(id) {
    updatePane(paneId, p => {
      if (p.tabs.length <= 1) return {}
      const next = p.tabs.find(t => t.id !== id)
      return {
        tabs: p.tabs.filter(t => t.id !== id),
        activeTabId: p.activeTabId === id ? next?.id : p.activeTabId
      }
    })
    postStateSnapshot()
  }

  // Resolves a widget's own id to the underlying GH object's guid (sourceId)
  // — every message to C# needs to name the GH object, never the widget,
  // since a control can now have more than one widget bound to it (Alt-drag
  // copies, see duplicateSlider below). Searches this pane's own tabs, which
  // is always enough here: every caller already got sliderId from a row this
  // pane is rendering. Falls back to sliderId itself for dev-seed items that
  // predate the id/sourceId split and never got a sourceId.
  function findSliderDeep(container, id) {
    const hit = container.sliders.find(s => s.id === id)
    if (hit) return hit
    for (const g of container.groups ?? []) {
      const found = findSliderDeep(g, id)
      if (found) return found
    }
    return null
  }
  // Preview-mode "Go to": double-clicking a row's name jumps the GH canvas to
  // the original object. Delegated here (every row type renders its label as
  // .name inside an element carrying data-slider-id) instead of wiring 13 rows.
  function onContentDblClick(e) {
    if ($mode !== 'preview') return
    if (!e.target.closest?.('.name')) return
    const id = e.target.closest('[data-slider-id]')?.dataset.sliderId
    if (id) postToCs({ type: 'goto_request', id: sourceIdFor(id) })
  }

  function sourceIdFor(sliderId) {
    for (const t of tabs) {
      const found = findSliderDeep(t, sliderId)
      if (found) return found.sourceId ?? found.id
    }
    return sliderId
  }

  // ── slider & group drag-drop (within pane) ────────────────────────────────────
  // Alt-drag: instead of moving the grabbed row, first spawn a copy right next
  // to it (same tab/group, same sourceId — see syncControl in layout.js for
  // how a shared sourceId keeps every copy's value/name in sync) and drag that
  // copy instead. The original never moves. Only makes sense for a single
  // slider-type row — a multi-selection or a group drag just moves as normal.
  function duplicateSlider(tabId, groupId, sliderId) {
    let cloneId = null
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== tabId) return t
      if (groupId) {
        return { ...t, groups: mapGroupTree(t.groups, groupId, g => {
          const original = orderedItems(g).find(it => it.kind === 'slider' && it.id === sliderId)?.data
          if (!original) return {}
          const clone = { ...original, id: crypto.randomUUID(), sourceId: original.sourceId ?? original.id }
          cloneId = clone.id
          return { sliders: insertSlidersAt(g, sliderId, 'after', [clone]).sliders }
        }) }
      }
      const original = orderedItems(t).find(it => it.kind === 'slider' && it.id === sliderId)?.data
      if (!original) return t
      const clone = { ...original, id: crypto.randomUUID(), sourceId: original.sourceId ?? original.id }
      cloneId = clone.id
      return insertSlidersAt(t, sliderId, 'after', [clone])
    }))
    return cloneId
  }

  function startDrag(type, id, fromTabId, fromGroupId = null, altKey = false) {
    // If the grabbed row is part of the current multi-selection, drag the whole
    // selection together — otherwise just this one item.
    let ids = (type === 'slider' && selectedIds.has(id) && selectedIds.size > 1)
      ? [...selectedIds]
      : [id]
    if (altKey && type === 'slider' && ids.length === 1) {
      const cloneId = duplicateSlider(fromTabId, fromGroupId, id)
      if (cloneId) { id = cloneId; ids = [cloneId] }
    }
    activeDrag = { type, id, ids, fromTabId, fromGroupId }
    itemDrag.set({ type, id, ids, fromPaneId: paneId, fromTabId, fromGroupId, fromWorkspaceId: get(activeWorkspaceId) })
  }
  function endDrag() {
    activeDrag = null; dropTarget = null; itemDrag.set(null); lastReorderKey = null
    if (reorderRaf) { cancelAnimationFrame(reorderRaf); reorderRaf = null }
  }
  function setDropTarget(type, id, pos = null, groupId = null) {
    if (!activeDrag) return
    dropTarget = { type, id, pos, groupId }
    if (type === 'slider-row' && activeDrag.type === 'slider') {
      const key = `${id}:${pos}:${groupId}`
      if (key !== lastReorderKey) {
        lastReorderKey = key
        // Defer the DOM-reordering mutation off the dragover event itself —
        // moving the hovered node mid-event confuses the browser's native
        // drag feedback and shows a "not-allowed" cursor. Doing it a frame
        // later lets the browser finish registering preventDefault() first.
        if (reorderRaf) cancelAnimationFrame(reorderRaf)
        reorderRaf = requestAnimationFrame(() => {
          reorderRaf = null
          if (activeDrag) liveReorderSlider(id, groupId, pos)
        })
      }
    } else if (activeDrag.type === 'group' && (type === 'slider-row' || (type === 'group-header' && pos !== 'nest'))) {
      // A dragged group can target either a slider row or another group's
      // header top/bottom band — both cases live-reorder the same way now
      // that sliders and groups share one order (see liveReorderGroup).
      const key = `group:${id}:${pos}`
      if (key !== lastReorderKey) {
        lastReorderKey = key
        if (reorderRaf) cancelAnimationFrame(reorderRaf)
        reorderRaf = requestAnimationFrame(() => {
          reorderRaf = null
          if (activeDrag) liveReorderGroup(id, groupId, pos)
        })
      }
    }
  }
  function clearDropTarget(type, id) {
    if (dropTarget?.type === type && dropTarget.id === id) dropTarget = null
  }

  function executeDrop(type, id, pos) {
    if (!activeDrag) return
    const { type: dt, id: di, ids: dragIds, fromTabId } = activeDrag
    if (type === 'tab') {
      if (fromTabId === id) { endDrag(); return }
      if (dt === 'slider') moveSlidersToTab(dragIds, fromTabId, id)
      else                 moveGroupToTab(di, fromTabId, id)
    } else if (type === 'group-header') {
      if (dt === 'slider') moveSlidersToGroup(dragIds, fromTabId, id)
      else if (dt === 'group') { if (pos === 'nest') nestGroupInGroup(di, fromTabId, id) }
      // pos 'before'/'after' for a group source already reordered live during dragover
    }
    // slider-row reordering already happened live during dragover — drop just finalizes
    endDrag()
    postStateSnapshot()
  }

  function withTab(tabId, fn) {
    mutateTabs(tabs => tabs.map(t => t.id === tabId ? fn(t) : t))
  }

  function moveSlidersToTab(sliderIds, fromTabId, toTabId) {
    const idSet = new Set(sliderIds)
    let moved = []
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        const [stripped, items] = extractSlidersByIds(t, idSet)
        moved = items
        return stripped
      })
      if (!moved.length) return tabs
      return t1.map(t => t.id === toTabId ? { ...t, sliders: [...t.sliders, ...withAppendedPositions(t, moved)] } : t)
    })
  }

  function moveGroupToTab(groupId, fromTabId, toTabId) {
    let group = null
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        group = t.groups.find(g => g.id === groupId)
        return { ...t, groups: t.groups.filter(g => g.id !== groupId) }
      })
      if (!group) return tabs
      return t1.map(t => t.id === toTabId ? { ...t, groups: [...t.groups, { ...group, pos: posAppend(t) }] } : t)
    })
  }

  // Reorders live, on every dragover hover change (not just on drop), so siblings
  // visibly shift out of the way as the drag progresses. Moves the whole dragged
  // block (activeDrag.ids — more than one when dragging a multi-selection) together,
  // wherever each member currently lives, into targetId's list (top-level or
  // targetGroupId's), preserving the block's relative order.
  function liveReorderSlider(targetId, targetGroupId, pos) {
    if (!activeDrag) return
    const dragIds = new Set(activeDrag.ids ?? [activeDrag.id])
    if (dragIds.has(targetId)) return

    mutateTabsQuiet(tabs => tabs.map(t => {
      if (t.id !== activeDrag.fromTabId) return t
      const [stripped, items] = extractSlidersByIds(t, dragIds)
      if (!items.length) return t
      if (targetGroupId) {
        return { ...stripped, groups: mapGroupTree(stripped.groups, targetGroupId, g => ({ sliders: insertSlidersAt(g, targetId, pos, items).sliders })) }
      }
      return insertSlidersAt(stripped, targetId, pos, items)
    }))

    activeDrag = { ...activeDrag, fromGroupId: targetGroupId }
  }

  function moveSlidersToGroup(sliderIds, fromTabId, targetGroupId) {
    const idSet = new Set(sliderIds)
    let moved = []
    mutateTabs(tabs => {
      const t1 = tabs.map(t => {
        if (t.id !== fromTabId) return t
        const [stripped, items] = extractSlidersByIds(t, idSet)
        moved = items
        return stripped
      })
      if (!moved.length) return tabs
      return t1.map(t => t.id !== fromTabId ? t : {
        ...t,
        groups: mapGroupTree(t.groups, targetGroupId, g => ({ sliders: [...g.sliders, ...withAppendedPositions(g, moved)] }))
      })
    })
  }

  // Places `group` immediately before/after targetId, within whichever
  // container targetGroupId points to (null = tab top-level) — position
  // comes from that container's unified slider+group order (orderedItems in
  // layout.js), so a dragged group can land next to a slider row too, not
  // just another group. No tree search needed: targetGroupId always arrives
  // explicitly on the drag event (GroupSection's containerId prop / slider
  // rows' existing groupId payload), mirroring insertSlidersAt.
  // Returns null if targetId isn't there (e.g. it was a descendant of the
  // group just extracted above, and vanished along with it) — callers must
  // treat that as "no-op for this hover", not "drop group at top level".
  function insertGroupAt(tab, targetGroupId, targetId, pos, group) {
    function place(container) {
      const ordered = orderedItems(container)
      const idx = ordered.findIndex(it => it.id === targetId)
      if (idx < 0) return null
      const neighborBefore = pos === 'after' ? ordered[idx]     : ordered[idx - 1]
      const neighborAfter  = pos === 'after' ? ordered[idx + 1] : ordered[idx]
      return [...(container.groups ?? []), { ...group, pos: posBetween(neighborBefore?.pos ?? null, neighborAfter?.pos ?? null) }]
    }
    if (targetGroupId == null) {
      const groups = place(tab)
      return groups ? { ...tab, groups } : null
    }
    let found = false
    const groups = mapGroupTree(tab.groups, targetGroupId, g => {
      const placed = place(g)
      if (!placed) return {}
      found = true
      return { groups: placed }
    })
    return found ? { ...tab, groups } : null
  }

  // Live sibling reorder while dragging a group over another item's row/
  // header edge (a slider row, or another group's header top/bottom band —
  // see GroupSection's headerZone) — splices the dragged group out and back
  // in on every hover change so everything visibly shifts as the drag
  // progresses, the same idea as liveReorderSlider.
  function liveReorderGroup(targetId, targetGroupId, pos) {
    if (!activeDrag || activeDrag.type !== 'group') return
    if (activeDrag.id === targetId) return
    mutateTabsQuiet(tabs => tabs.map(t => {
      if (t.id !== activeDrag.fromTabId) return t
      const [withoutSource, source] = extractGroupFromTree(t.groups, activeDrag.id)
      if (!source) return t
      return insertGroupAt({ ...t, groups: withoutSource }, targetGroupId, targetId, pos, source) ?? t
    }))
  }

  function nestGroupInGroup(sourceGroupId, fromTabId, targetGroupId) {
    if (sourceGroupId === targetGroupId) return
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== fromTabId) return t
      const [withoutSource, source] = extractGroupFromTree(t.groups, sourceGroupId)
      if (!source) return t
      // targetGroupId may itself have been a descendant of the dragged group
      // (and so vanished along with it) — mapGroupTree would then silently
      // leave the tree unchanged, dropping `source` on the floor. Track
      // whether it actually matched before committing the mutation.
      let found = false
      const groups = mapGroupTree(withoutSource, targetGroupId, g => {
        found = true
        return { groups: [...(g.groups ?? []), { ...source, pos: posAppend(g) }] }
      })
      if (!found) return t
      return { ...t, groups }
    }))
  }

  // ── selection ─────────────────────────────────────────────────────────────────
  // ctrl toggles a single row in/out of the selection. shift selects the
  // whole run between the anchor (lastClickedId) and this row — resolved via
  // rangeIds, so it follows visual order through nested groups AND expands
  // any group caught in the range to its full contents (see rangeIds above;
  // shared with onGroupSelect so the anchor can be a slider or a group
  // either way round). The anchor only ever comes from the active tab's own
  // order, so a range can't reach across tabs. Falls through to a plain
  // single-select if shift has no usable anchor yet (nothing clicked before,
  // or same row).
  function onSliderSelect(sliderId, shift, ctrl) {
    if ($mode !== 'edit') return
    if (shift && lastClickedId && lastClickedId !== sliderId) {
      const ids = rangeIds(lastClickedId, sliderId)
      if (ids) {
        selectedIds = new Set([...selectedIds, ...ids])
        lastClickedId = sliderId
        return
      }
    }
    if (ctrl) {
      const next = new Set(selectedIds)
      if (next.has(sliderId)) next.delete(sliderId); else next.add(sliderId)
      selectedIds = next
      lastClickedId = sliderId
      return
    }
    selectedIds = selectedIds.has(sliderId) && selectedIds.size === 1 ? new Set() : new Set([sliderId])
    lastClickedId = sliderId
  }
  function clearSelection() { selectedIds = new Set(); lastClickedId = null }

  // ── ActionBar ─────────────────────────────────────────────────────────────────
  function moveSelectedTo(targetTabId) {
    if (!selectedIds.size || !activeTab) return
    // Root sliders only — a selected whole group's cascaded-in descendants
    // (see onGroupSelect) shouldn't get shredded out of it by a cross-tab
    // move the group itself doesn't support yet, so those ids are dropped.
    const info = itemInfo(activeTab, null, new Map())
    const sliderIds = [...selectedIds].filter(id => {
      const meta = info.get(id)
      return meta && meta.kind === 'slider' && !selectedIds.has(meta.parentId)
    })
    if (!sliderIds.length) return
    moveSlidersToTab(sliderIds, activeTab.id, targetTabId)
    selectedIds = new Set()
  }

  // Maps every slider AND group id in a tab to {parentId, pos, kind} —
  // parentId is the id of whichever group directly contains it (null for
  // top-level). Feeds groupSelected's "roots" filter below: a selected id
  // whose parent is ITSELF selected doesn't get extracted on its own — it
  // rides along inside its already-selected-whole parent group instead
  // (see onGroupSelect's cascade). Also used by moveSelectedTo to keep a
  // cross-tab move to root sliders only, instead of shredding sliders out
  // of an intact selected group.
  function itemInfo(container, parentId, map) {
    for (const s of container.sliders) map.set(s.id, { parentId, pos: s.pos ?? 0, kind: 'slider' })
    for (const g of container.groups ?? []) {
      map.set(g.id, { parentId, pos: g.pos ?? 0, kind: 'group' })
      itemInfo(g, g.id, map)
    }
    return map
  }

  function groupSelected() {
    if (!selectedIds.size) return
    const idSet = selectedIds
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== activeTab.id) return t
      const info = itemInfo(t, null, new Map())
      // Roots = selected ids with no selected ancestor — a whole selected
      // group is one root (its cascaded-in descendants are skipped here,
      // they move as part of it), a lone selected slider is its own root.
      const roots = [...idSet].filter(id => info.has(id) && !idSet.has(info.get(id).parentId))
      if (!roots.length) return t
      const rootSliderIds = new Set(roots.filter(id => info.get(id).kind === 'slider'))
      const rootGroupIds  = roots.filter(id => info.get(id).kind === 'group')
      // Roots sharing one parent (a specific group, or top-level) → nest the
      // new group there. Roots spanning multiple containers have no single
      // sensible home, so it falls back to the tab's top level.
      const parents = new Set(roots.map(id => info.get(id).parentId))
      const targetGroupId = parents.size === 1 ? [...parents][0] : null
      const newPos = Math.min(...roots.map(id => info.get(id).pos))
      // Pull each selected whole group out intact (as a unit, not flattened)
      // before stripping sliders, so a group-of-groups nests groups inside
      // groups rather than merging everyone's sliders into one flat list.
      let working = t
      const extractedGroups = []
      for (const gid of rootGroupIds) {
        const [remaining, removed] = extractGroupFromTree(working.groups, gid)
        working = { ...working, groups: remaining }
        if (removed) extractedGroups.push(removed)
      }
      const [stripped, extractedSliders] = extractSlidersByIds(working, rootSliderIds)
      const newGroup = { id: 'g_' + Date.now(), label: 'Group', collapsed: false, previewShow: true, sliders: extractedSliders, groups: extractedGroups, pos: newPos }
      if (targetGroupId) {
        const groups = mapGroupTree(stripped.groups, targetGroupId, g => ({
          groups: [...(g.groups ?? []), newGroup]
        }))
        return { ...stripped, groups }
      }
      return { ...stripped, groups: [...stripped.groups, newGroup] }
    }))
    selectedIds = new Set()
    lastClickedId = null
  }

  // Depth-first, top-to-bottom {id, kind} order for a tab — same walk as
  // flattenSliderIds but keeping group entries too (each immediately
  // followed by its own contents), so a ctrl+shift range can land on a
  // group and know to pull in everything below it.
  function flattenItemIds(container) {
    const out = []
    for (const item of orderedItems(container)) {
      out.push({ id: item.id, kind: item.kind })
      if (item.kind === 'group') out.push(...flattenItemIds(item.data))
    }
    return out
  }

  function findGroupNode(groups, groupId) {
    for (const g of groups) {
      if (g.id === groupId) return g
      const found = findGroupNode(g.groups ?? [], groupId)
      if (found) return found
    }
    return null
  }

  // A group's own id plus every id nested inside it (sliders and subgroups,
  // all depths) — what a ctrl-click on the group marks selected together,
  // since the group is chosen as one whole unit, not just its header row.
  function groupAndDescendantIds(g) {
    const ids = [g.id]
    for (const s of g.sliders) ids.push(s.id)
    for (const sub of g.groups ?? []) ids.push(...groupAndDescendantIds(sub))
    return ids
  }

  // Shared by slider- and group- ctrl+shift range-select: walks the tab's
  // full visual order between fromId and toId. A slider entry contributes
  // its own id; a group entry pulls in its own id plus its entire subtree,
  // same as a direct ctrl-click on that group would — so a group anywhere
  // in the range comes in whole rather than being cut off wherever the
  // range boundary happens to land inside it.
  function rangeIds(fromId, toId) {
    const order = flattenItemIds(activeTab)
    const iFrom = order.findIndex(e => e.id === fromId)
    const iTo   = order.findIndex(e => e.id === toId)
    if (iFrom === -1 || iTo === -1) return null
    const [lo, hi] = iFrom < iTo ? [iFrom, iTo] : [iTo, iFrom]
    const ids = new Set()
    for (let i = lo; i <= hi; i++) {
      const entry = order[i]
      if (entry.kind === 'group') {
        const gNode = findGroupNode(activeTab.groups, entry.id)
        if (gNode) groupAndDescendantIds(gNode).forEach(id => ids.add(id))
      } else {
        ids.add(entry.id)
      }
    }
    return ids
  }

  // Ctrl-click toggles the whole group (itself + everything inside it) in or
  // out of the selection as one unit. Ctrl+shift-click range-selects from
  // the last clicked id (slider or group) through this group, via rangeIds.
  function onGroupSelect(groupId, shift) {
    if ($mode !== 'edit') return
    if (shift && lastClickedId && lastClickedId !== groupId) {
      const ids = rangeIds(lastClickedId, groupId)
      if (ids) {
        selectedIds = new Set([...selectedIds, ...ids])
        lastClickedId = groupId
        return
      }
    }
    const node = findGroupNode(activeTab.groups, groupId)
    if (!node) return
    const ids = groupAndDescendantIds(node)
    const allSelected = ids.every(id => selectedIds.has(id))
    const next = new Set(selectedIds)
    if (allSelected) ids.forEach(id => next.delete(id))
    else ids.forEach(id => next.add(id))
    selectedIds = next
    lastClickedId = groupId
  }

  // ── group helpers (recursive for nested groups) ───────────────────────────────
  function toggleGroup(groupId) {
    mutateTabs(tabs => tabs.map(t => ({
      ...t,
      groups: mapGroupTree(t.groups, groupId, g => ({ collapsed: !g.collapsed }))
    })))
  }
  // Group-level "show" is a gate over its (possibly nested) items' own
  // previewPinned flags, not a force-on — Blender collection-visibility
  // model per user request: group hidden → nothing under it shows regardless
  // of item flags; group shown (the default — missing/undefined counts as
  // shown, so existing groups from before this feature aren't silently
  // hidden) → items decide for themselves. See ApplyPreviewPins in
  // SlateWindow.cs for the matching AND-gate walk.
  function togglePreviewPin(groupId) {
    mutateTabs(tabs => tabs.map(t => ({
      ...t,
      groups: mapGroupTree(t.groups, groupId, g => ({ previewShow: !(g.previewShow ?? true) }))
    })))
  }
  function renameGroup(groupId, label) {
    mutateTabs(tabs => tabs.map(t => ({
      ...t,
      groups: mapGroupTree(t.groups, groupId, () => ({ label }))
    })))
  }
  function removeGroup(groupId) {
    mutateTabs(tabs => tabs.map(t => {
      if (t.id !== activeTab.id) return t
      // Top-level: promote sliders + sub-groups to tab
      const topIdx = t.groups.findIndex(g => g.id === groupId)
      if (topIdx >= 0) {
        const rem = t.groups[topIdx]
        const remaining = { ...t, groups: t.groups.filter(g => g.id !== groupId) }
        // Promoted items keep their relative order to each other but are
        // reassigned fresh pos values (appended after whatever's already at
        // the tab's top level, sliders first then sub-groups) — their old
        // pos was only meaningful inside the removed group's own container.
        const promotedSliders = withAppendedPositions(remaining, rem.sliders ?? [])
        const afterSliders = { ...remaining, sliders: [...remaining.sliders, ...promotedSliders] }
        const promotedGroups = withAppendedPositions(afterSliders, rem.groups ?? [])
        return {
          ...remaining,
          sliders: afterSliders.sliders,
          groups:  [...remaining.groups, ...promotedGroups]
        }
      }
      // Nested: just remove (no promotion)
      return { ...t, groups: removeFromGroupTree(t.groups, groupId) }
    }))
  }

  // ── slider/group removal ──────────────────────────────────────────────────────
  function removeSlider(tabId, sliderId) {
    mutateTabs(tabs => tabs.map(t =>
      t.id !== tabId ? t : {
        ...t,
        sliders: t.sliders.filter(s => s.id !== sliderId),
        groups:  removeSliderFromGroups(t.groups, sliderId)
      }
    ))
    const next = new Set(selectedIds); next.delete(sliderId); selectedIds = next
  }

  // ── corner drag (split / collapse) ───────────────────────────────────────────
  // Split creates the pane immediately on the first confirmed intent
  // (CornerHandle already applies its own 8px lock threshold before it ever
  // dispatches 'split', so this doesn't need a separate one), then live-resizes
  // it for the rest of the drag — instead of the old preview-overlay-then-
  // commit-on-release flow, which felt laggy/indirect.
  //
  // Window-level listeners because splitPane() turns this pane's leaf node
  // into a split (this whole Pane instance + its CornerHandles get destroyed
  // and replaced), which drops whatever pointer capture CornerHandle was
  // holding — the eventual pointerup would otherwise never reach us. An
  // earlier version of this feature called splitPane() on every preview tick
  // instead of once and hit exactly this problem (see "Corner drag tek akış"
  // in Çözülen Problemler) — calling it once, then handing off to window
  // listeners, avoids it.
  // Guards the 'split' branch below against firing twice for one drag.
  // It's supposed to fire exactly once because splitPane() turns this
  // pane's leaf into a split (the leaf's own node type flips straight from
  // 'leaf' to 'split' at this exact tree position), which should destroy
  // this Pane instance and its CornerHandles before another pointermove
  // can arrive.
  let splitInFlight = false

  function onCornerPreview(detail) {
    if (!detail) {
      hoverHint.set(null)
      return
    }
    if (detail.kind === 'collapse') {
      const neighborId = findNeighborPane($layout, paneId, detail.dir, detail.side)
      hoverHint.set(neighborId ? 'Release to collapse the neighbouring pane' : null)
      return
    }

    // kind === 'split' — must fire exactly once per drag (see splitInFlight).
    if (splitInFlight) return
    splitInFlight = true

    // Alt held: the new pane spans the whole window edge rather than just
    // carving up this one pane, so the drag ratio has to be measured against
    // the whole window, not this pane's own (possibly small, off-center) rect.
    const spanning = detail.spanning
    const rect = spanning ? document.querySelector('.layout-root').getBoundingClientRect() : paneEl.getBoundingClientRect()
    const dim  = detail.dir === 'h' ? rect.width : rect.height
    const myOrigin = detail.dir === 'h' ? rect.left : rect.top
    const newId = newSplitId()

    // Same unconditional center + aligned-edge snap SplitDivider's own drag
    // uses (splitSnap.js) — applied from the very first frame so a freshly
    // created split snaps immediately instead of only once you grab its
    // divider afterward. The new divider doesn't exist yet on the very first
    // call (before splitPane() below), so it just finds nothing to exclude.
    let lastSnapLabel = ''
    const ratioAt = (clientX, clientY) => {
      const raw0 = detail.dir === 'h' ? (clientX - rect.left) : (clientY - rect.top)
      const excludeEl = document.querySelector(`.divider.dir-${detail.dir}[data-split-id="${newId}"]`)
      const targets = computeAlignedSnapTargets(detail.dir, myOrigin, excludeEl)
      const { raw, label } = snapRaw(raw0, dim, targets)
      lastSnapLabel = label
      return clampRatio(raw / dim)
    }

    if (spanning) {
      splitPaneSpanning(detail.dir, detail.side, ratioAt(detail.clientX, detail.clientY) * dim, newId)
    } else {
      splitPane(paneId, detail.dir, detail.side, ratioAt(detail.clientX, detail.clientY) * dim, newId)
    }
    hoverHint.set(`Split ${detail.dir === 'h' ? 'vertically' : 'horizontally'}${spanning ? ' (whole window)' : ''} — drag to resize`)

    // If pointerup/pointercancel never reaches window (host app steals focus
    // mid-drag — e.g. Rhino's own window grabbing focus during a live GH
    // solve — or the OS/WebView2 otherwise swallows the release), these
    // listeners used to leak permanently: every future mouse move anywhere
    // in the app would keep calling setSplitSize() on THIS split, silently
    // resizing an unrelated pane. 'blur' is the same safety net the old
    // (removed) altHeld tracking used for an analogous reason — commit at
    // the last known position and clean up rather than leave the drag
    // dangling forever.
    function onWindowMove(e) {
      const size = ratioAt(e.clientX, e.clientY) * dim
      setSplitSize(newId, size)
      hoverHint.set(`${Math.round(size)}px${lastSnapLabel}`)
    }
    function onWindowUp(e) {
      if (e) onWindowMove(e)   // final position from the release coords themselves ('blur' has none)
      window.removeEventListener('pointermove', onWindowMove)
      window.removeEventListener('pointerup', onWindowUp)
      window.removeEventListener('pointercancel', onWindowUp)
      window.removeEventListener('blur', onWindowUp)
      splitInFlight = false
      hoverHint.set(null)
      postStateSnapshot()
    }
    window.addEventListener('pointermove', onWindowMove)
    window.addEventListener('pointerup', onWindowUp)
    window.addEventListener('pointercancel', onWindowUp)
    window.addEventListener('blur', onWindowUp)
  }

  function onCornerCommit(detail) {
    if (!detail || detail.kind !== 'collapse') return
    const neighborId = findNeighborPane($layout, paneId, detail.dir, detail.side)
    if (neighborId) collapsePane(neighborId)
    postStateSnapshot()
  }

  function clampRatio(r) { return Math.max(0.1, Math.min(0.9, r)) }

  // ── cross-pane tab drag ───────────────────────────────────────────────────────
  import { moveTab, splitWithTab } from '../stores/layout.js'

  function onTabDragStart(tabId, label) {
    tabDrag.set({ tabId, fromPaneId: paneId, fromWorkspaceId: get(activeWorkspaceId), label })
  }
  function onTabDragEnd() {
    tabDrag.set(null)
    edgeZone = null
  }

  function onPaneDragOver(e) {
    const isTabDrag  = $tabDrag  && $tabDrag.fromPaneId  !== paneId
    const isItemDrag = $itemDrag && $itemDrag.fromPaneId !== paneId
    if (!isTabDrag && !isItemDrag) return
    e.preventDefault()
    if (isItemDrag) itemDragHover = true
    if (!paneEl || !isTabDrag) return
    const rect = paneEl.getBoundingClientRect()
    const x = (e.clientX - rect.left) / rect.width
    const y = (e.clientY - rect.top)  / rect.height
    const edge = 0.2
    if      (x < edge)       edgeZone = 'left'
    else if (x > 1 - edge)   edgeZone = 'right'
    else if (y < edge)       edgeZone = 'top'
    else if (y > 1 - edge)   edgeZone = 'bottom'
    else                     edgeZone = 'center'
  }

  function onPaneDragLeave(e) {
    if (!paneEl?.contains(e.relatedTarget)) { edgeZone = null; itemDragHover = false }
  }

  function onPaneDrop(e) {
    e.preventDefault()
    if ($tabDrag && $tabDrag.fromPaneId !== paneId && edgeZone) {
      const { tabId, fromPaneId, fromWorkspaceId } = $tabDrag
      const toWorkspaceId = get(activeWorkspaceId)
      if (edgeZone === 'center') {
        moveTab(fromWorkspaceId ?? toWorkspaceId, fromPaneId, tabId, toWorkspaceId, paneId)
      } else {
        const dir  = (edgeZone === 'left' || edgeZone === 'right') ? 'h' : 'v'
        const side = (edgeZone === 'left' || edgeZone === 'top')   ? 'before' : 'after'
        splitWithTab(fromWorkspaceId ?? toWorkspaceId, fromPaneId, tabId, toWorkspaceId, paneId, dir, side)
      }
      tabDrag.set(null)
      edgeZone = null
      postStateSnapshot()
      return
    }
    if ($itemDrag && $itemDrag.fromPaneId !== paneId && activeTabId) {
      dropItemOnTab(activeTabId)
      return
    }
    edgeZone = null
    itemDragHover = false
  }

  // Cross-pane item drop targeted at a specific tab button (possibly not the
  // destination pane's active tab) — lets a drag land straight in the right
  // tab instead of always going to whichever tab happens to be open there,
  // which used to force a drop-then-switch-tab-then-move-again round trip.
  let itemDropTabId = null   // tab id currently highlighted while a cross-pane item drag hovers this pane's TabBar
  $: if (!$itemDrag) itemDropTabId = null

  function dropItemOnTab(tabId) {
    if (!$itemDrag || $itemDrag.fromPaneId === paneId) return
    const { type, id, ids, fromPaneId: fp, fromTabId: ft, fromWorkspaceId } = $itemDrag
    moveCrossPaneItem(fromWorkspaceId ?? get(activeWorkspaceId), fp, ft, ids ?? [id], type, get(activeWorkspaceId), paneId, tabId)
    itemDrag.set(null)
    itemDragHover = false
    itemDropTabId = null
    postStateSnapshot()
  }

  $: canDragTabs        = true
  $: isDraggingTab      = $tabDrag !== null
  $: isDropTarget       = isDraggingTab && $tabDrag?.fromPaneId !== paneId
  $: crossPaneItemDrag  = $itemDrag !== null && $itemDrag.fromPaneId !== paneId
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div
  class="pane"
  data-pane-id={paneId}
  bind:this={paneEl}
  style="border: 1px solid {edgeTint}{paneStyleCss}"
  class:drop-target={isDropTarget || itemDragHover}
  on:dragover={onPaneDragOver}
  on:dragleave={onPaneDragLeave}
  on:drop={onPaneDrop}
  on:contextmenu={onPaneContextMenu}
>
  {#if $mode === 'edit'}
    <CornerHandle corner="tl" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
    <CornerHandle corner="tr" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
    <CornerHandle corner="bl" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
    <CornerHandle corner="br" on:preview={e => onCornerPreview(e.detail)} on:commit={e => onCornerCommit(e.detail)} />
  {/if}

  <header>
    <TabBar
      {tabs} {activeTabId} mode={$mode}
      isDragging={activeDrag !== null}
      dropTabId={dropTarget?.type === 'tab' ? dropTarget.id : itemDropTabId}
      {canDragTabs}
      {crossPaneItemDrag}
      on:select={e        => { setActive(e.detail); clearSelection() }}
      on:rename={e        => renameTab(e.detail.id, e.detail.label)}
      on:colorChange={e   => setTabColor(e.detail.id, e.detail.color)}
      on:remove={e        => removeTab(e.detail)}
      on:add={addTab}
      on:tabDragOver={e   => setDropTarget('tab', e.detail)}
      on:tabDragLeave={e  => clearDropTarget('tab', e.detail)}
      on:tabDrop={e       => executeDrop('tab', e.detail)}
      on:tabDragStart={e  => onTabDragStart(e.detail.tabId, e.detail.label)}
      on:tabDragEnd={onTabDragEnd}
      on:itemDragOverTab={e  => itemDropTabId = e.detail}
      on:itemDragLeaveTab={e => { if (itemDropTabId === e.detail) itemDropTabId = null }}
      on:itemDropOnTab={e    => dropItemOnTab(e.detail)}
      on:capture={() => postToCs({ type: 'capture', tabId: activeTabId })}
    />

    {#if $mode === 'edit' && selectedIds.size > 0}
      <ActionBar
        count={selectedIds.size} {tabs} activeTabId={activeTabId}
        on:moveTo={e         => moveSelectedTo(e.detail)}
        on:groupSelected={groupSelected}
        on:clearSelection={clearSelection}
      />
    {/if}
  </header>

  <!-- svelte-ignore a11y-mouse-events-have-key-events -->
  <section class="content"
    style="--name-col-w: {nameColWidth}px"
    on:dragenter|preventDefault={e => activeDrag && (e.dataTransfer.dropEffect = 'move')}
    on:dragover|preventDefault={e  => activeDrag && (e.dataTransfer.dropEffect = 'move')}
    on:mouseover={onContentMouseOver}
    on:mouseout={onContentMouseOut}
    on:dblclick={onContentDblClick}
  >
    {#if !activeTab || (activeTab.sliders.length === 0 && activeTab.groups.length === 0)}
      <div class="empty">
        {#if $mode === 'edit'}
          Select sliders in Grasshopper, then click <strong>Capture</strong>.
        {:else}
          No sliders in this tab.
        {/if}
      </div>
    {:else}
      {#each activeItems as item, i (item.id)}
        <div class="row-slot" animate:flip={{ duration: resizingSliderId ? 0 : 150, easing: cubicOut }}>
          {#if item.kind === 'slider'}
            {@const slider = item.data}
            <svelte:component
              this={ROW_COMPONENTS[slider.type] ?? SliderRow}
              {slider} mode={$mode}
              selected={selectedIds.has(slider.id)}
              isFirst={i === 0}
              isLast={i === activeItems.length - 1}
              on:change={e      => onSliderChange(slider.id, e.detail, slider.type, slider.multiSelect)}
              on:commit={e      => onSliderCommit(slider.id, e.detail, slider.type)}
              on:resize={e       => onPanelResize(slider.id, e.detail, false)}
              on:resizeCommit={e => onPanelResize(slider.id, e.detail, true)}
              on:resizeStart={e  => resizingSliderId = e.detail}
              on:resizeEnd={()   => resizingSliderId = null}
              on:select={e      => onSliderSelect(slider.id, e.detail.shift, e.detail.ctrl)}
              on:textAlign={e => onTextAlignChange(slider.id, e.detail)}
              on:textColor={e => onTextColorChange(slider.id, e.detail)}
              on:textFontSize={e => onTextFontSizeChange(slider.id, e.detail)}
              on:textBold={e => onTextBoldChange(slider.id, e.detail)}
              on:textItalic={e => onTextItalicChange(slider.id, e.detail)}
              on:textUnderline={e => onTextUnderlineChange(slider.id, e.detail)}
              on:paramViewerSize={e       => onParamViewerSizeChange(slider.id, e.detail)}
              on:paramViewerShowCounts={e => onParamViewerShowCountsChange(slider.id, e.detail)}
              on:remove={() => removeSlider(activeTab.id, slider.id)}
              on:dragStart={e => startDrag('slider', slider.id, activeTab.id, null, e.detail)}
              on:contextMenu={e => onSliderContextMenu(e.detail, slider)}
              on:rowDragOver={e => setDropTarget('slider-row', slider.id, e.detail, null)}
              on:rowDragLeave={() => clearDropTarget('slider-row', slider.id)}
              on:rowDrop={e     => executeDrop('slider-row', slider.id, e.detail)}
              on:dragEnd={endDrag}
            />
          {:else}
            {@const group = item.data}
            <GroupSection
              {group} mode={$mode} {selectedIds} {dropTarget} {resizingSliderId} {activeDrag}
              dropHighlight={dropTarget?.type === 'group-header' && dropTarget.id === group.id && activeDrag?.type === 'slider'}
              dropNest={dropTarget?.type === 'group-header' && dropTarget.id === group.id && dropTarget.pos === 'nest' && activeDrag?.type === 'group'}
              dropBeforeMe={dropTarget?.type === 'group-header' && dropTarget.id === group.id && dropTarget.pos === 'before' && activeDrag?.type === 'group'}
              dropAfterMe={dropTarget?.type === 'group-header' && dropTarget.id === group.id && dropTarget.pos === 'after' && activeDrag?.type === 'group'}
              on:toggle={e              => toggleGroup(e.detail)}
              on:groupSelect={e         => onGroupSelect(e.detail.id, e.detail.shift)}
              on:rename={e              => renameGroup(e.detail.id, e.detail.label)}
              on:togglePreviewPin={e     => togglePreviewPin(e.detail)}
              on:remove={e              => removeGroup(e.detail)}
              on:sliderChange={e        => onSliderChange(e.detail.id, e.detail.value, e.detail.type, e.detail.multiSelect)}
              on:sliderCommit={e        => onSliderCommit(e.detail.id, e.detail.value, e.detail.type)}
              on:sliderResize={e        => onPanelResize(e.detail.id, e.detail.height, false)}
              on:sliderResizeCommit={e  => onPanelResize(e.detail.id, e.detail.height, true)}
              on:sliderResizeStart={e   => resizingSliderId = e.detail}
              on:sliderResizeEnd={()    => resizingSliderId = null}
              on:sliderSelect={e        => onSliderSelect(e.detail.id, e.detail.shift, e.detail.ctrl)}
              on:sliderTextAlign={e     => onTextAlignChange(e.detail.id, e.detail.align)}
              on:sliderTextColor={e     => onTextColorChange(e.detail.id, e.detail.color)}
              on:sliderTextFontSize={e  => onTextFontSizeChange(e.detail.id, e.detail.size)}
              on:sliderTextBold={e      => onTextBoldChange(e.detail.id, e.detail.value)}
              on:sliderTextItalic={e    => onTextItalicChange(e.detail.id, e.detail.value)}
              on:sliderTextUnderline={e => onTextUnderlineChange(e.detail.id, e.detail.value)}
              on:sliderParamViewerSize={e       => onParamViewerSizeChange(e.detail.id, e.detail.size)}
              on:sliderParamViewerShowCounts={e => onParamViewerShowCountsChange(e.detail.id, e.detail.value)}
              on:sliderRemove={e        => removeSlider(activeTab.id, e.detail.sliderId)}
              on:sliderContextMenu={e   => onSliderContextMenu(e.detail.pos, e.detail.slider)}
              on:headerDragStart={e => startDrag('group', e.detail, activeTab.id, null)}
              on:headerDragOver={e => setDropTarget('group-header', e.detail.id, e.detail.pos, e.detail.groupId)}
              on:headerDragLeave={e => clearDropTarget('group-header', e.detail)}
              on:headerDrop={e => executeDrop('group-header', e.detail.id, e.detail.pos)}
              on:groupDragEnd={endDrag}
              on:sliderDragStart={e      => startDrag('slider', e.detail.sliderId, activeTab.id, e.detail.groupId, e.detail.altKey)}
              on:sliderDragEnd={endDrag}
              on:sliderRowDragOver={e    => setDropTarget('slider-row', e.detail.sliderId, e.detail.pos, e.detail.groupId)}
              on:sliderRowDragLeave={e   => clearDropTarget('slider-row', e.detail.sliderId)}
              on:sliderRowDrop={e        => executeDrop('slider-row', e.detail.sliderId, e.detail.pos)}
              on:capture={e              => postToCs({ type: 'capture', tabId: activeTabId, groupId: e.detail.groupId })}
            />
          {/if}
        </div>
      {/each}
    {/if}
  </section>

  <!-- cross-pane item drag overlay (only when hovering) -->
  {#if itemDragHover}
    <div class="drop-overlay zone-center"></div>
  {/if}

  <!-- cross-pane tab drop overlay -->
  {#if isDropTarget}
    <div class="drop-overlay"
      class:zone-left={edgeZone === 'left'}
      class:zone-right={edgeZone === 'right'}
      class:zone-top={edgeZone === 'top'}
      class:zone-bottom={edgeZone === 'bottom'}
      class:zone-center={edgeZone === 'center'}
    >
      {#if edgeZone && edgeZone !== 'center'}
        <div class="split-preview split-{edgeZone}"></div>
      {/if}
    </div>
  {/if}
</div>

{#if contextMenu}
  <ContextMenu x={contextMenu.x} y={contextMenu.y} items={contextMenu.items} on:close={() => contextMenu = null} />
{/if}

{#if paneSettingsPopup}
  <PaneSettingsPopup x={paneSettingsPopup.x} y={paneSettingsPopup.y}
    bg={pane?.style?.bg ?? null} pattern={pane?.style?.pattern ?? 'none'}
    patternScale={pane?.style?.patternScale ?? PANE_PATTERN_DEFAULTS.scale}
    patternOpacity={pane?.style?.patternOpacity ?? PANE_PATTERN_DEFAULTS.opacity}
    on:change={onPaneSettingsChange}
    on:close={() => paneSettingsPopup = null} />
{/if}

<style>
  /* .pane paints the per-pane override ONCE — header stays transparent (see
     below) and just shows this same layer through, rather than repainting
     its own copy on top. header USED to carry its own copy of these same
     properties (see paneStyleCss's comment for the older bug that motivated
     it — header showing the theme default because it read a hardcoded
     var(--bg), not this override, at all), but re-painting an IDENTICAL
     translucent colour a second time on the exact area .pane already
     painted compounds the alpha: fully-opaque colours hid this, but any
     custom colour with reduced opacity came out visibly darker/different in
     the header strip than in the body below it. A single paint layer also
     sidesteps the diagonal/crosshatch pattern seam at the header/body
     boundary — two independently-tiled copies could drift out of phase;
     one continuous image can't. */
  .pane {
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
    position: relative;
    overflow: hidden;
    border-radius: 4px;
    background-color: var(--pane-bg-override, var(--bg));
    /* border colour set inline from edgeTint (JS/$theme-driven, not a CSS
       var — see edgeTint's comment above) */
    background-image: var(--pane-pattern-image-override, none);
    background-size: var(--pane-pattern-size-override, auto);
  }

  header {
    flex-shrink: 0;
    background: transparent;
  }

  .content {
    flex: 1;
    overflow-y: auto;
    padding: 6px 0;
  }
  .row-slot { display: block; }
  .content::-webkit-scrollbar       { width: 4px; }
  .content::-webkit-scrollbar-track { background: transparent; }
  .content::-webkit-scrollbar-thumb { background: var(--grid); border-radius: 2px; }

  .empty {
    padding: 36px 20px;
    color: rgba(var(--text-rgb), 0.3);
    font-size: 12px;
    line-height: 1.7;
  }
  .empty strong { color: rgba(var(--text-rgb), 0.45); font-weight: 500; }

  /* cross-pane drop overlay */
  .drop-overlay {
    position: absolute;
    inset: 0;
    pointer-events: none;
    z-index: 15;
    border: 2px solid transparent;
    transition: border-color 0.1s;
  }
  .drop-target .drop-overlay  { border-color: rgba(var(--accent-rgb), 0.27); }
  .zone-center .drop-overlay,
  .drop-overlay.zone-center   { border-color: var(--accent); background: rgba(var(--accent-rgb), 0.07); }

  .split-preview {
    position: absolute;
    background: rgba(var(--accent-rgb), 0.2);
    border: 2px solid var(--accent);
    pointer-events: none;
  }
  .split-preview.split-left   { inset: 0 50% 0 0; }
  .split-preview.split-right  { inset: 0 0 0 50%; }
  .split-preview.split-top    { inset: 0 0 50% 0; }
  .split-preview.split-bottom { inset: 50% 0 0 0; }
</style>
