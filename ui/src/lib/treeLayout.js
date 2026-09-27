// Pure layout/data helpers for the Data Tree Explorer's sunburst view.
//
// Tree shape (generic — not GH-specific, so a real Path Mapper capture can
// hand in the same shape later without touching this file):
//   leaf:     { count: number }         — a real branch, count = item count
//   internal: { kids: Node[] }          — a grouping level, no data of its own
// A node's path is never stored on the node itself — it's implicit from its
// position among its ancestors' `kids`, and callers reconstruct it as an
// array of indices (e.g. [0, 1, 2] ~ GH's `{0;1;2}`).

export function isLeaf(node) {
  return !node.kids
}

export function totalCount(node) {
  return isLeaf(node) ? node.count : node.kids.reduce((s, k) => s + totalCount(k), 0)
}

export function branchCount(node) {
  return isLeaf(node) ? 1 : node.kids.reduce((s, k) => s + branchCount(k), 0)
}

export function maxDepth(node) {
  return isLeaf(node) ? 0 : node.kids.reduce((m, k) => Math.max(m, 1 + maxDepth(k)), 0)
}

export function nodeAtPath(root, path) {
  let n = root
  for (const i of path) {
    if (isLeaf(n) || !n.kids[i]) return null
    n = n.kids[i]
  }
  return n
}

export function pathLabel(path) {
  return '{' + path.join(';') + '}'
}

// Angular weight — empty subtrees still get a thin sliver instead of
// collapsing to zero width and disappearing from the ring entirely.
const EMPTY_WEIGHT = 0.35
function weight(node) {
  return isLeaf(node) ? node.count + EMPTY_WEIGHT : node.kids.reduce((s, k) => s + weight(k), 0)
}

function polar(cx, cy, r, a) {
  return [cx + r * Math.cos(a), cy + r * Math.sin(a)]
}

// A single SVG arc command can't draw a full circle — when a wedge's span is
// exactly 2π (e.g. one branch holding 100% of the ring, or drilling into a
// node with only one child), its start and end point are the literal same
// coordinate, which most renderers treat as a zero-length no-op instead of
// a full ring (2026-09-27 kullanıcı: "sadece 1 branch olduğunda gösterim
// bozuluyor, tek bir dilim şeklinde gösterebilmeli"). Nudging the end angle
// a hair short of a full turn keeps start/end distinct with no visible seam.
const FULL_CIRCLE_EPS = 0.0001

function arcPath(cx, cy, r0, r1, a0, a1) {
  if (a1 - a0 >= 2 * Math.PI - FULL_CIRCLE_EPS) a1 = a0 + 2 * Math.PI - FULL_CIRCLE_EPS
  const [x0, y0] = polar(cx, cy, r1, a0)
  const [x1, y1] = polar(cx, cy, r1, a1)
  const [x2, y2] = polar(cx, cy, r0, a1)
  const [x3, y3] = polar(cx, cy, r0, a0)
  const large = a1 - a0 > Math.PI ? 1 : 0
  return `M${x0} ${y0}A${r1} ${r1} 0 ${large} 1 ${x1} ${y1}L${x2} ${y2}A${r0} ${r0} 0 ${large} 0 ${x3} ${y3}Z`
}

// Recursive angular partition of `root`'s children into concentric rings —
// one ring per depth level below root, arc width proportional to subtree
// weight. `root` itself is never drawn (it's just a container for whatever
// is currently focused — see DataTreeSunburst's drill-in) — only its `kids`
// are distributed around the full circle, each becoming its own subtree.
//
// Ring count adapts to the depth actually present in `root` — drilling into
// a shallow branch of a deep tree reserves radius for just that branch's
// remaining depth, not the whole tree's.
export function computeSunburst(root, { cx, cy, holeR, outerR, ringGap = 3 }) {
  const kids = root.kids ?? []
  const depth = Math.max(0, ...kids.map(maxDepth))
  const ringCount = Math.max(1, depth + 1)
  const ringStep = (outerR - holeR) / ringCount
  const arcs = []

  function place(node, path, a0, a1, level) {
    const r0 = holeR + level * ringStep
    const r1 = r0 + ringStep - (level < ringCount - 1 ? ringGap : 0)
    const leaf = isLeaf(node)
    arcs.push({
      path, level, a0, a1, mid: (a0 + a1) / 2, r0, r1,
      d: arcPath(cx, cy, r0, r1, a0, a1),
      count: leaf ? node.count : totalCount(node),
      empty: leaf && node.count === 0,
      isLeaf: leaf,
    })
    if (leaf) return
    const kidsWeights = node.kids.map(weight)
    const total = kidsWeights.reduce((s, w) => s + w, 0) || 1
    let a = a0
    node.kids.forEach((k, i) => {
      const span = (a1 - a0) * (kidsWeights[i] / total)
      place(k, [...path, i], a, a + span, level + 1)
      a += span
    })
  }

  const weights = kids.map(weight)
  const total = weights.reduce((s, w) => s + w, 0) || 1
  let a = -Math.PI / 2
  kids.forEach((k, i) => {
    const span = 2 * Math.PI * (weights[i] / total)
    place(k, [i], a, a + span, 0)
    a += span
  })

  return { arcs, ringCount }
}

export { polar }
