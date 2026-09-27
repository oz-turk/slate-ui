<script>
  import { computeSunburst, nodeAtPath, totalCount, branchCount, pathLabel, polar } from './treeLayout.js'
  import { postToCs } from './ipc.js'
  import { paramViewerItemsCache } from '../stores/uiState.js'

  export let tree        // root node: { kids: [...] } — see treeLayout.js
  export let size = 460  // rendered px (square)
  export let id   = null // captured paramViewer's own id — needed to request leaf items
  // "(N = X)" suffix on/off (2026-09-26 kullanıcı: "N sayısının graph'ta
  // gösterilmesi için bir toggle ekleyelim, default açık olsun") — leaves
  // everything else (path labels, pins, leader lines) untouched, only the
  // count text itself.
  export let showCounts = true

  // Drill-in (design exploration turn 3, option "3a"): clicking any non-leaf
  // arc recenters the view on it, filling the whole circle with just that
  // subtree; `focusPath` is absolute from the true tree root so labels stay
  // meaningful even after drilling in. Resets whenever a different tree is
  // handed in (e.g. a different captured param).
  let focusPath = []
  let prevTree = null
  $: if (tree !== prevTree) { prevTree = tree; focusPath = []; itemsPath = null }

  // Drilling into a LEAF (2026-09-26: "bir level daha içeri girip dilimler
  // halinde görmek istiyordum") — reuses the exact same ring engine, just
  // fed a synthetic one-ring node built from the fetched values instead of
  // real tree structure, so each item becomes its own equal wedge (same
  // visual language as everything else — no separate list UI, per "dil
  // bütünlüğü olsun"). Values come from GetParamViewerLeafItems on demand
  // (paramViewer_request_items in SlateWindow.cs) — not sent eagerly with
  // the tree, since a branch can hold thousands of items. `itemsPath` is
  // null while showing the normal tree ring.
  let itemsPath = null
  $: itemsKey = itemsPath ? `${id}:${itemsPath.join(',')}` : null
  $: items = itemsKey ? $paramViewerItemsCache[itemsKey] : undefined
  let requestedKeys = new Set()
  $: if (itemsKey && items === undefined && !requestedKeys.has(itemsKey)) {
    requestedKeys.add(itemsKey)
    postToCs({ type: 'paramViewer_request_items', id, path: itemsPath })
  }
  $: itemsNode = itemsPath ? { kids: (items ?? []).map(() => ({ count: 1 })) } : null

  $: focusNode = itemsNode ?? nodeAtPath(tree, focusPath) ?? tree

  // Same 520×520/center-260 coordinate scale as design exploration turn 3's
  // "inside {2}" (option 3a, zoomB) — OUTER_R matches its leaf ring's own r1
  // (176) exactly, deliberately leaving the same generous ~84px of outer
  // margin for the pins/label ring beyond it (2026-09-26: "dilimlerden biraz
  // uzak olması daha iyi olur" / "zip'teki mesafelere kayın kurabilirsen").
  const CX = 260, CY = 260, HOLE_R = 40, OUTER_R = 176

  // Fixed, theme-independent grey ramp (2026-09-26: "fill'de transparency
  // olmasın, böylelikle dark ve light temada da aynı renk çıkar") — mixing
  // var(--text) into var(--bg) still tracked the active theme, which isn't
  // "the same color" the user asked for. Originally the zip's own three
  // literal fills verbatim (root '#d7d3d3', mid '#7d7979', leaf '#2d2b2b')
  // — but those were calibrated against the zip's fixed LIGHT paper
  // background (#f3f2f2), and root's near-white glared against Slate's
  // dark theme (--bg #1a1a1a; kullanıcı ekran görüntüsü, 2026-09-26).
  // Retuned to true monochrome (R=G=B exactly, no zip-original warm bias
  // either) with a narrower lightness range that stays comfortable against
  // BOTH Slate's dark (#1a1a1a) and light (#e4e1d8) backgrounds, per
  // kullanıcı: "monocrom olacak şekilde yapsak daha iyi olmaz mı".
  const LEVEL_FILLS = ['#9c9c9c', '#6e6e6e', '#404040']
  function levelFill(level) {
    return LEVEL_FILLS[Math.min(level, LEVEL_FILLS.length - 1)]
  }
  // Contrast for the "inside the wedge" path label (fitsMid roomy case) —
  // it sits directly ON a fixed LEVEL_FILLS colour now (not the pane's own
  // background), so the theme-reactive --text token can't be trusted to
  // read well against it in every theme (e.g. dark ink on the dark leaf
  // fill, in light theme, would be low-contrast). The rim-placed label
  // doesn't need this — it sits in the blank margin, on the actual theme
  // background, where --text already contrasts correctly.
  function levelTextFill(level) {
    return level < 2 ? '#1a1a1a' : '#f0f0f0'
  }

  $: ({ arcs } = computeSunburst(focusNode, { cx: CX, cy: CY, holeR: HOLE_R, outerR: OUTER_R }))
  $: totalItems = totalCount(focusNode)
  $: totalBranches = branchCount(focusNode)

  // Drilling into a LEAF recenters on its fetched items instead of a
  // sub-tree (2026-09-26: "bir level daha içeri girip dilimler halinde
  // görmek istiyordum") — see itemsPath/itemsNode above.
  function drillTo(arc) {
    if (itemsPath) return // already showing items — nothing further to drill into
    if (arc.isLeaf) {
      if (!arc.empty) itemsPath = [...focusPath, ...arc.path]
      return
    }
    focusPath = [...focusPath, ...arc.path]
  }
  function goBack() {
    if (itemsPath) { itemsPath = null; return }
    focusPath = focusPath.slice(0, -1)
  }

  // Rim label chain (leader stub → path label → N label) — one number in,
  // the rest follow, so lengthening the stub doesn't need three separate
  // matching edits (2026-09-27 kullanıcı: "çizgi uzarsa path uzaklaşacak,
  // path uzaklaşırsa N uzaklaşacak... basit bir hesaplama ekleyemez
  // miyiz"). All still guessed constants, not real text measurement — see
  // where they're used below for the tuning history.
  const LEADER_START    = 3   // gap from the wedge's own edge to the stub's start
  const LEADER_LEN      = 18  // the stub's own length (2026-09-27: doubled from 9 as an experiment)
  // Was miscopied as 3 when this got chained together — the actual gap the
  // old hardcoded pathR (r1+24) left past the old stub's tip (r1+12) was 12,
  // not 3, so the chained version put the path label almost on top of the
  // stub (2026-09-27 kullanıcı: "çizgiyi uzattığımız için şimdi path'in
  // içine girdi, öncekindeki gibi boşluk olmalıydı"). Restored to 12.
  const LABEL_GAP       = 12  // gap from the stub's tip to where the path label starts
  const PATH_COUNT_GAP  = 20  // gap from the path label to the N label

  // Rough "is there room to letter this arc" check — chord length in px.
  function fits(arc, minPx) {
    return arc.r1 * (arc.a1 - arc.a0) > minPx
  }
  // Same idea, but at the arc's OWN mid-radius rather than its outer edge —
  // used to decide whether a leaf's path label can sit inside the wedge
  // itself (2026-09-26: "pathleri dilimlerin içine yazalım", for a wide
  // single-ring wedge with room to spare) instead of the outside-rim
  // treatment reserved for thin/crowded arcs.
  function fitsMid(arc, minPx) {
    return ((arc.r0 + arc.r1) / 2) * (arc.a1 - arc.a0) > minPx
  }
  function labelPos(arc, r) {
    return polar(CX, CY, r, arc.mid)
  }
  // A plain curved line (no fill, no separate radial ticks) at a fixed
  // radius spanning one wedge's own angular width — 2026-09-26: "noktaları
  // gösterirken artık dikey çizgiler çizmeye gerek yok, dilim boyunca
  // noktaları birleştiren bir arc çizelim, dilim sayısınca arc olacak".
  function ringArcPath(r, a0, a1) {
    const [x0, y0] = polar(CX, CY, r, a0)
    const [x1, y1] = polar(CX, CY, r, a1)
    const large = a1 - a0 > Math.PI ? 1 : 0
    return `M${x0} ${y0}A${r} ${r} 0 ${large} 1 ${x1} ${y1}`
  }
  function labelRotation(arc, x, y) {
    const deg = (arc.mid * 180) / Math.PI
    const flip = Math.cos(arc.mid) < 0
    return `rotate(${flip ? deg + 180 : deg} ${x} ${y})`
  }
  function arcKey(arc) {
    return arc.level + ':' + arc.path.join(',')
  }
  // True path from the real tree root (not just relative to focusNode) —
  // the only unambiguous identifier once you're several levels deep, and
  // what design exploration turn 3's "inside {2}" view (option 3a) labels
  // every arc with, leaf or not. In item-view mode there's no GH path per
  // wedge (each one IS a single fetched value) — show that instead.
  //
  function arcLabel(arc) {
    if (itemsPath) return items?.[arc.path[0]] ?? '…'
    return pathLabel([...focusPath, ...arc.path])
  }
  // GH's OWN native notation for a branch's count (2026-09-26: "grasshopper'
  // daki doğru gösterimi ne" — verified via ilspycmd against
  // GH_ParamViewer.HtmlHelp_Source(), whose own worked example literally
  // reads "path {0;0} (N = 4)") — folds the on-arc count number (removed as
  // a separate element) into the one label that's already guaranteed to
  // survive a crowded/non-fitting wedge. A separate <tspan> (not just
  // appended to arcLabel) so it can carry its own colour/weight (2026-09-26:
  // "ufak bir nüans eklersek iyi olur, okunurluk açısından").
  function arcCountSuffix(arc) {
    return ` (N = ${arc.count})`
  }

  // Hover highlight — 2026-09-26: "mavi outline yerine fill rengi değişecek"
  // (was an accent-blue border, kept getting overpainted at shared edges no
  // matter how many passes; solid orange fill is simpler AND matches the
  // zip's own "picked up" piece treatment). Tracked here since the border
  // pass is a separate element from the fill (see arcKey(arc) + ':fill'/
  // ':stroke' below), so hovering the fill still needs to reach the label
  // pass too (nothing currently reacts to it there, but kept generic).
  let hoveredKey = null

  // One radial pin per item in a leaf branch (design exploration turn 3,
  // option "3a"'s "one pin per item on the rim") — gives a leaf's count a
  // concrete, countable shape instead of collapsing straight to a number.
  // Bails to the plain numeric label (rendered by the caller) once pins
  // would be closer together than PIN_MIN_PX at this arc's own angular
  // width — we only have a count from GH, not real per-item values, so
  // packing them past legibility would just be noise.
  const PIN_MIN_PX = 3.5
  // Matches .arc-stroke's own stroke-width — reused so a leaf's pin ring
  // sits exactly as far from its wedge as one wedge sits from the next
  // (2026-09-26 kullanıcı: "dilimlerden uzaklığı biraz fazla, iki dilim
  // arasındaki boşluk kadar bir mesafe yeterli"), and so neighbouring
  // leaves' pin rings get the same visible separation from EACH OTHER as
  // the wedges themselves do (see pinArcSpan below) — without it they're
  // angularly contiguous (a leaf's a1 = the next leaf's a0) and read as one
  // unbroken circle instead of one arc per branch.
  const WEDGE_GAP = 3
  const PIN_DOT_R = 1.6
  // Insets a wedge-wide pin ring's own angular span by WEDGE_GAP (converted
  // from a linear px gap to radians at this radius) so it stops short of
  // its neighbours, capped so it can't invert on a very thin wedge.
  function pinArcSpan(arc, r) {
    const inset = Math.min(WEDGE_GAP / r, ((arc.a1 - arc.a0) / 2) * 0.9)
    return [arc.a0 + inset, arc.a1 - inset]
  }
  function pinAngles(arc) {
    const n = arc.count
    if (n <= 0) return []
    const span = arc.a1 - arc.a0
    if ((span * arc.r1) / n < PIN_MIN_PX) return []
    const angles = []
    for (let i = 0; i < n; i++) angles.push(arc.a0 + ((i + 0.5) / n) * span)
    return angles
  }
  // Same "does it fit" math as pinAngles' own internal check, but as a
  // reusable predicate — see midFitsByLevel/pinsFitByLevel below for why.
  function pinsFit(arc) {
    if (arc.count <= 0) return true   // nothing to fit, so it can't drag the group down
    return (arc.a1 - arc.a0) * arc.r1 / arc.count >= PIN_MIN_PX
  }

  // Per-ring (level) all-or-nothing gate for both the inline path+N label
  // and the per-item pins — deciding "does this fit" arc by arc let
  // neighbours at the SAME radius end up in different styles the moment
  // live item counts changed one wedge's width but not another's, which
  // read as the whole ring "tangling" together (2026-09-27 kullanıcı: "path
  // ve N değerini sığdırabiliyorsa dilimin içine sığdırmaya çalışıyor...
  // eğer noktalardan biri bile sığmıyorsa diğerlerini de koyma, eğer
  // pathlerden biri bile sığmıyorsa diğerlerini de koyma"). One arc failing
  // its level's fit check now falls the WHOLE level back to the
  // crowded/rim treatment, so a ring always reads as one consistent style.
  $: leafArcsByLevel = (() => {
    const map = new Map()
    for (const arc of arcs) {
      if (!arc.isLeaf) continue
      if (!map.has(arc.level)) map.set(arc.level, [])
      map.get(arc.level).push(arc)
    }
    return map
  })()
  $: midFitsByLevel  = new Map([...leafArcsByLevel].map(([level, list]) => [level, list.every(a => fitsMid(a, 19))]))
  $: pinsFitByLevel  = new Map([...leafArcsByLevel].map(([level, list]) => [level, list.every(pinsFit)]))
</script>

<div class="tree-sunburst" style="width:{size}px; height:{size}px">
  <svg viewBox="0 0 520 520" width={size} height={size}>
    <!-- Fill pass — every arc's solid area, click/hover/tooltip live here.
         Border is a SEPARATE pass below: SVG paints in document order, so
         drawing fill+stroke together per arc let whichever sibling/child
         arc happened to paint next cover the earlier arc's shared-edge
         stroke. Splitting the passes means every stroke always sits above
         every fill, so borders read consistently everywhere. -->
    {#each arcs as arc (arcKey(arc) + ':fill')}
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <!-- svelte-ignore a11y-click-events-have-key-events -->
      <path
        d={arc.d}
        class="arc-fill"
        class:leaf={arc.isLeaf}
        class:empty={arc.empty}
        class:drillable={!arc.isLeaf}
        fill={arc.empty ? 'transparent' : hoveredKey === arcKey(arc) ? '#ec3013' : levelFill(arc.level)}
        on:click={() => drillTo(arc)}
        on:mouseenter={() => hoveredKey = arcKey(arc)}
        on:mouseleave={() => hoveredKey = null}
      >
        <title>{arc.isLeaf ? (itemsPath ? arcLabel(arc) : arcLabel(arc) + arcCountSuffix(arc)) : `${arcLabel(arc)} — ${arc.count} item${arc.count === 1 ? '' : 's'}`}</title>
      </path>
    {/each}

    <!-- Empty branches get NO dashed special-case anymore (2026-09-26:
         "kesikli çizgi kullanma sadece fill'i olmasın yeter") — same solid
         border as everything else, just a transparent fill (see the fill
         pass above) so the pane shows through. -->
    {#each arcs as arc (arcKey(arc) + ':stroke')}
      <path d={arc.d} class="arc-stroke" />
    {/each}

    <!-- Item-view is ONE flat list, not separate branches — one continuous
         circle around the centre instead of a broken arc per wedge
         (2026-09-26: "burada artık tek liste olduğu için tek circle olacak
         ama bir önceki durumda [normal ağaç] {0;0} için ayrı yay, {0;1}
         için ayrı yay çizilmeli" — so the per-wedge arc below stays for the
         real-tree case, this replaces it only in item-view). -->
    {#if itemsPath && arcs.length}
      <circle cx={CX} cy={CY} r={arcs[0].r0 - WEDGE_GAP - PIN_DOT_R} class="pin-arc" fill="none" />
    {/if}

    {#each arcs as arc (arcKey(arc) + ':detail')}
      {#if arc.isLeaf}
        {@const pins = (arc.empty || !pinsFitByLevel.get(arc.level)) ? [] : pinAngles(arc)}
        <!-- Pin ring sits WEDGE_GAP + a dot's own radius past the wedge edge
             (2026-09-26 kullanıcı: "dilimlerden uzaklığı biraz fazla, iki
             dilim arasındaki boşluk kadar bir mesafe yeterli, o mesafeye
             noktanın yarıçapını da ekle") — was a flat +24, much farther
             than the wedge-to-wedge gap itself. One connecting arc per wedge
             instead of a radial tick per item (2026-09-26: "noktaları
             gösterirken artık dikey çizgiler çizmeye gerek yok, dilim
             boyunca noktaları birleştiren bir arc çizelim, dilim sayısınca
             arc olacak") — the dots alone already read as "N members"; the
             arc just threads them together as one wedge-wide mark instead of
             a forest of separate radial lines. Its own span is inset by
             pinArcSpan (same WEDGE_GAP) so neighbouring leaves' rings don't
             read as one unbroken circle (2026-09-26 kullanıcı: "her branch
             için ayrı yay çizilsin, görselde sadece bir circle var") — plain
             a0..a1 was angularly contiguous with the next leaf's a0, so
             adjacent pin arcs touched with no visible break.
             IN ITEM-VIEW (each wedge already IS one item), that outer "N
             members" ring says nothing new — moved to the INNER edge
             instead, right around the centre breadcrumb (2026-09-26: "artık
             item'ları listelediğimizde dışarda noktaların olması anlamsız,
             onu içeriye alalım, path/x items/back'in etrafında olsun". Offset
             is INWARD from the wedges' own inner radius, by the same
             WEDGE_GAP + PIN_DOT_R used outward on the other side (2026-09-26
             kullanıcı: "noktaların bulunduğu circle offseti de dilimler
             arasındaki mesafe kadar olmalı, dışardaki arc'lar için
             uyguladığımız mesafe gibi") — was a bare -8 (itself a fix for an
             earlier +3 that sat ON the dividers, which only exist for
             r >= r0). -->
        {@const pinR = itemsPath ? arc.r0 - WEDGE_GAP - PIN_DOT_R : arc.r1 + WEDGE_GAP + PIN_DOT_R}
        {#if pins.length && !itemsPath}
          {@const [pa0, pa1] = pinArcSpan(arc, pinR)}
          <path d={ringArcPath(pinR, pa0, pa1)} class="pin-arc" fill="none" />
        {/if}
        {#each pins as ang, i (i)}
          {@const [dx, dy] = polar(CX, CY, pinR, ang)}
          <!-- Orange dot — 2026-09-26: "belki turuncu nokta olarak
               kullanabiliriz, bir üye olduğu daha net anlaşılır". -->
          <circle cx={dx} cy={dy} r={PIN_DOT_R} class="pin-dot" />
        {/each}
        {#if midFitsByLevel.get(arc.level)}
          <!-- Plenty of room in EVERY leaf at this ring, not just this one
               wedge (2026-09-27: whole-level gate, see midFitsByLevel) — put
               the path right inside it, same treatment as a non-leaf arc,
               instead of the outside-rim label (2026-09-26: "pathleri
               dilimlerin içine yazalım"). -->
          {@const [lx, ly] = labelPos(arc, (arc.r0 + arc.r1) / 2)}
          <!-- Same left-hemisphere flip labelRotation itself adds (cos(mid)<0)
               to keep the label upright — rotating the WHOLE run an extra
               180° for readability also reverses which tspan a viewer reads
               first, so "path then N" (2026-09-27 kullanıcı: "önce path
               sonra N kuralımız burada bozulmuş, 180-360 arası flip olmuş")
               silently became "N then path" on that half unless the DOM
               order is swapped to match. Same fix the rim/leader-line case
               already needed for its own near/far version of this. -->
          {@const flip = Math.cos(arc.mid) < 0}
          <!-- Inline `style` (not the `fill` attribute) so it actually beats
               the .lbl-count CSS rule's own fill — CSS always outranks a
               plain presentation attribute. -->
          <text x={lx} y={ly} class="lbl idx" style="fill:{levelTextFill(arc.level)}" text-anchor="middle" dominant-baseline="middle"
              transform={labelRotation(arc, lx, ly)}>
            {#if !itemsPath && showCounts}
              {#if flip}
                <tspan class="lbl-count" style="fill:{levelTextFill(arc.level)}; opacity:0.7">{arcCountSuffix(arc)}</tspan><tspan dx="6">{arcLabel(arc)}</tspan>
              {:else}
                <tspan>{arcLabel(arc)}</tspan><tspan class="lbl-count" dx="6" style="fill:{levelTextFill(arc.level)}; opacity:0.7">{arcCountSuffix(arc)}</tspan>
              {/if}
            {:else}
              <tspan>{arcLabel(arc)}</tspan>
            {/if}
          </text>
        {:else}
          <!-- On-arc count number REMOVED here (2026-09-26: "sığmayan
               dilimler için item sayısını gizleyelim ama path ve dik çizgi
               gizlenmesin") — a non-fitting wedge now shows only the rim
               path label + leader below, never a bare count. -->
          <!-- Rim path label — always drawn now, no min-width gate
               (2026-09-26 kullanıcı: "bazı dilimlerde path yazılmamış, sığmaz
               diye endişe edilmiş ama line ile uzatarak eklediğimiz için
               rahatça sığar, öyle bir limit eklemeyelim" — the earlier
               fits(arc,14) check assumed a too-thin wedge couldn't fit the
               label, but the label lives OUTSIDE the wedge on the leader
               line, at a fixed radius unrelated to the wedge's own angular
               width, so that width was never actually the constraint).
               Full absolute path (not a short suffix) so it's unambiguous at
               any depth. A single "leader" stem from the
               wedge's OWN centre (arc.mid, not spread across the span like
               the per-item pins) points at it — visually distinct from the
               per-item pins/dots so the two don't read as the same kind of
               mark (2026-09-26: "inside 2'dekine benzer ama biraz farklı
               olmalı").
               Path and count used to be two <tspan>s inside ONE rotated
               text run, centred as a single block — which silently broke
               "path always near, N always far" (2026-09-26 kullanıcı: "dilime
               yakın tarafta hep path olmalı, uzak tarafta N olmalı"): the
               label is rotated by the arc's own angle so its run direction
               IS the radial direction, and on the flipped (left-hemisphere)
               half an extra 180° gets added for readability, which reverses
               which end of the run points outward — so the FIRST tspan
               (path) landed near on the right half but far on the left half.
               Splitting into two independently-positioned <text>s at fixed
               radii makes near=path/far=N a geometric fact instead of a
               rotation-direction side effect. Also shortens the leader line
               itself (kullanıcı: "çizgi çok uzun") and gives it a small,
               fixed gap on both ends (kullanıcı: "dilimlerle arasındaki
               boşluk kadar path'lerle de arasında boşluk olmalı") — still no
               real getBBox() measurement (rejected earlier as too heavy for
               a few px of gap), so the gap to the text is an estimate, not
               exact for every string length.
               First attempt used only +14 between pathR/countR and the two
               overlapped (2026-09-26 kullanıcı ekran görüntüsü) — because
               the text is rotated to run ALONG the radial line, its
               occupied radial space is its rendered WIDTH (a handful of
               characters, ~25-35+ units), not its font height (~9px); +14
               was sized like a line-height gap, which is the wrong axis
               entirely.
               pathR/countR/the leader stub used to be three separately
               tuned constants — every "too close/too far" round meant
               re-deriving all three by hand. Now chained off one number
               (LEADER_LEN, see top of script): the stub's own tip sets
               where the path label starts, which sets where the N label
               starts. Still guessed distances, not real text measurement. -->
          {@const leaderTipR = arc.r1 + LEADER_START + LEADER_LEN}
          {@const pathR = leaderTipR + LABEL_GAP}
          {@const countR = pathR + PATH_COUNT_GAP}
          {@const [lx1, ly1] = polar(CX, CY, arc.r1 + LEADER_START, arc.mid)}
          {@const [lx2, ly2] = polar(CX, CY, leaderTipR, arc.mid)}
          <line x1={lx1} y1={ly1} x2={lx2} y2={ly2} class="leader" />
          {@const [px, py] = labelPos(arc, pathR)}
          <text x={px} y={py} class="lbl idx" text-anchor="middle" dominant-baseline="middle"
              transform={labelRotation(arc, px, py)}>{arcLabel(arc)}</text>
          {#if !itemsPath && showCounts}
            {@const [nx, ny] = labelPos(arc, countR)}
            <text x={nx} y={ny} class="lbl-count" text-anchor="middle" dominant-baseline="middle"
                transform={labelRotation(arc, nx, ny)}>{arcCountSuffix(arc)}</text>
          {/if}
        {/if}
      {:else}
        {@const [lx, ly] = labelPos(arc, (arc.r0 + arc.r1) / 2)}
        <text x={lx} y={ly} class="lbl idx" text-anchor="middle" dominant-baseline="middle"
            transform={labelRotation(arc, lx, ly)}>
          {arcLabel(arc)}
        </text>
      {/if}
    {/each}

    <!-- Centre hole doubles as the breadcrumb (design exploration's "the
         centre turns into the breadcrumb"): at root it's the grand total,
         drilled in it's the focused path + a one-level-back hint. -->
    {#if focusPath.length === 0 && !itemsPath}
      <text x={CX} y={CY - 6} class="lbl total" text-anchor="middle" dominant-baseline="middle">{totalItems}</text>
      <text x={CX} y={CY + 10} class="lbl total-caption" text-anchor="middle" dominant-baseline="middle">
        {totalBranches} branch{totalBranches === 1 ? '' : 'es'}
      </text>
    {:else}
      <text x={CX} y={CY - 10} class="lbl path" text-anchor="middle" dominant-baseline="middle">{pathLabel(itemsPath ?? focusPath)}</text>
      <text x={CX} y={CY + 6} class="lbl total-caption" text-anchor="middle" dominant-baseline="middle">
        {totalItems} item{totalItems === 1 ? '' : 's'}
      </text>
      <!-- svelte-ignore a11y-click-events-have-key-events -->
      <!-- svelte-ignore a11y-no-static-element-interactions -->
      <text x={CX} y={CY + 21} class="lbl back" text-anchor="middle" dominant-baseline="middle" on:click={goBack}>‹ back</text>
    {/if}
  </svg>
</div>

<style>
  .tree-sunburst { display: flex; justify-content: center; }

  svg { display: block; overflow: visible; }

  .arc-fill.drillable { cursor: pointer; }
  .arc-fill { transition: fill 0.08s; }

  .arc-stroke {
    fill: none;
    stroke: var(--pane-bg-override-opaque, var(--bg));
    stroke-width: 3;
    pointer-events: none;
  }

  .pin-arc {
    stroke: rgba(var(--text-rgb), 0.4);
    stroke-width: 1.2;
    pointer-events: none;
  }
  .pin-dot { fill: #ec3013; pointer-events: none; }

  .leader {
    stroke: rgba(var(--text-rgb), 0.35);
    stroke-width: 1;
    pointer-events: none;
  }

  .lbl {
    fill: var(--text);
    /* Condensed/technical stack (2026-09-27 kullanıcı: "daha dar bir font...
       çiplerin üstünde dar alanda rahat sığsın diye kullanılan fontlar
       gibi") — Bahnschrift ships with Windows (this app is Windows-only,
       see e.g. PanelRow's Segoe UI Mono usage) and its SemiCondensed
       instance is registered as its own selectable family name there, so a
       narrower path/count label costs no extra width vs the app's base
       Segoe UI without it. Falls back to the app's normal sans stack if
       unavailable. */
    font-family: 'Bahnschrift SemiCondensed', 'Bahnschrift', 'Segoe UI', system-ui, sans-serif;
    pointer-events: none;
  }
  .lbl.idx { font-size: 9px; font-weight: 600; fill: rgba(var(--text-rgb), 0.7); }
  .lbl-count {
    /* Doesn't carry the .lbl class (the rim case renders it as its own
       standalone <text>, not nested inside one) so it never inherited
       .lbl's font-family — only the path label was actually getting
       Bahnschrift before this (2026-09-27 kullanıcı: "fontu hem path'te
       hem N'de mi değiştirdin?" — no, just path). Same stack, explicit. */
    font-family: 'Bahnschrift SemiCondensed', 'Bahnschrift', 'Segoe UI', system-ui, sans-serif;
    font-size: 6px;
    font-weight: 400;
    font-style: italic;
    letter-spacing: -0.2px;
    fill: rgba(var(--text-rgb), 0.45);
  }
  .lbl.total { font-size: 24px; font-weight: 800; }
  .lbl.total-caption { font-size: 9px; font-weight: 600; letter-spacing: 0.06em; fill: rgba(var(--text-rgb), 0.5); text-transform: uppercase; }
  .lbl.path { font-size: 16px; font-weight: 800; fill: var(--accent); }
  .lbl.back {
    font-size: 9px;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    fill: var(--accent);
    cursor: pointer;
    pointer-events: all;
  }
  .lbl.back:hover { text-decoration: underline; }
</style>
