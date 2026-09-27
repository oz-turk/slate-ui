// Dev-only mock data for the Data Tree Explorer (ParamViewerRow /
// DataTreeSunburst) — stands in for a real captured param's tree until Path
// Mapper capture exists (see yapilacaklar/data-tree-editor.md). Only ever
// reached from `import.meta.env.DEV`-gated code (see stores/layout.js) so it
// never ships in the built .gha bundle.

function seededRng(seed) {
  let s = seed
  return () => (s = (s * 9301 + 49297) % 233280) / 233280
}

// Builds a random nested tree `levels` deep — each internal node branches
// into [minKids, maxKids] children, each leaf holds a random item count in
// [0, maxItems] (0 = an empty branch, on purpose; real GH trees have those
// too). Shape: `{ kids: Node[] }` for internal nodes, `{ count }` for leaves
// — see treeLayout.js for what consumes this.
export function generateMockTree({ seed = 1, levels = 3, minKids = 2, maxKids = 4, maxItems = 12, emptyChance = 0.12 } = {}) {
  const rnd = seededRng(seed)
  function build(depth) {
    if (depth === levels) {
      const empty = rnd() < emptyChance
      return { count: empty ? 0 : 1 + Math.floor(rnd() * maxItems) }
    }
    const n = minKids + Math.floor(rnd() * (maxKids - minKids + 1))
    return { kids: Array.from({ length: n }, () => build(depth + 1)) }
  }
  return build(0)
}

// A crowded variant, for testing the drill-in zoom (far more leaf branches
// than the default sample) — mirrors the design exploration's turn-3 "dense"
// sample tree used to make the ring-crowding problem real.
export function generateDenseMockTree(seed = 2) {
  return generateMockTree({ seed, levels: 3, minKids: 5, maxKids: 8, maxItems: 30, emptyChance: 0.08 })
}
