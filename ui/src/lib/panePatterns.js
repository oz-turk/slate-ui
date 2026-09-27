// Named pane background-pattern presets (Pane Settings popup, right-click a
// pane) — a fixed, predetermined set rather than freeform, same approach as
// button/toggle appearance variants. Keyed the same as pane.style.pattern.
// 'none' is the real, explicit default (no more 'theme'/inherit option —
// removed 2026-09-22 along with the beta theme's own automatic --pane-pattern
// CSS var in app.css, which made "what's actually showing" ambiguous against
// what this popup had selected).
//
// image/size are functions, not fixed strings — Pane Settings also exposes a
// Scale and Opacity slider per pane (pane.style.patternScale/patternOpacity).
// image(opacityPct, scale) covers both: tile-based presets (dots/grid/
// checker) only need opacity here and do the scaling via their own size()
// (background-size), since that's the CSS-native way to resize a tile.
// repeating-gradient presets (diagonal/crosshatch/rings) have no tile to
// resize that way — their repeat spacing is baked into the gradient string
// itself, so they use the scale argument directly and leave size() as 'auto'.
export const PANE_PATTERN_DEFAULTS = { scale: 1, opacity: 5 }   // opacity in %, matches the old hardcoded 0.05

// Tint colour is var(--pattern-tint-rgb) (app.css), not a hardcoded white —
// white lines are invisible against light mode's near-white pane surfaces,
// same reasoning as --edge-tint/--ws-tab-bg there (white in dark, black in
// light). Resolves fine even though these strings land in an inline style
// (Pane.svelte's paneStyleCss) — var() resolves against the element's
// computed style at paint time regardless of where the declaration came from.

export const PANE_PATTERNS = {
  none: { image: () => 'none', size: () => 'auto' },
  dots: {
    image: opacityPct => `radial-gradient(rgba(var(--pattern-tint-rgb), ${opacityPct / 100}) 1px, transparent 1px)`,
    size: scale => `${Math.round(18 * scale)}px ${Math.round(18 * scale)}px`,
  },
  grid: {
    image: opacityPct => `linear-gradient(rgba(var(--pattern-tint-rgb), ${opacityPct / 100}) 1px, transparent 1px), linear-gradient(90deg, rgba(var(--pattern-tint-rgb), ${opacityPct / 100}) 1px, transparent 1px)`,
    size: scale => `${Math.round(20 * scale)}px ${Math.round(20 * scale)}px`,
  },
  checker: {
    image: opacityPct => {
      const c = `rgba(var(--pattern-tint-rgb), ${opacityPct / 100})`
      return `linear-gradient(45deg, ${c} 25%, transparent 25%), linear-gradient(-45deg, ${c} 25%, transparent 25%), linear-gradient(45deg, transparent 75%, ${c} 75%), linear-gradient(-45deg, transparent 75%, ${c} 75%)`
    },
    size: scale => { const t = Math.round(16 * scale); return `${t}px ${t}px` },
  },
  diagonal: {
    image: (opacityPct, scale) => {
      const c = `rgba(var(--pattern-tint-rgb), ${opacityPct / 100})`
      const gap = Math.round(10 * scale)
      return `repeating-linear-gradient(45deg, ${c} 0, ${c} 1px, transparent 1px, transparent ${gap}px)`
    },
    size: () => 'auto',
  },
  crosshatch: {
    image: (opacityPct, scale) => {
      const c = `rgba(var(--pattern-tint-rgb), ${opacityPct / 100})`
      const gap = Math.round(10 * scale)
      return `repeating-linear-gradient(45deg, ${c} 0, ${c} 1px, transparent 1px, transparent ${gap}px), repeating-linear-gradient(-45deg, ${c} 0, ${c} 1px, transparent 1px, transparent ${gap}px)`
    },
    size: () => 'auto',
  },
  rings: {
    // Anchored "at 0 0" (top-left), not the default "center" — with
    // background-size 'auto' the gradient's implicit image size is each
    // element's OWN box, so a centered circle sits at a different absolute
    // point for header (~20px tall) vs. the much taller pane body, and the
    // two independently-tiled copies (see Pane.svelte's shared
    // --pane-pattern-image-override comment) meet at a visible seam.
    // header is the pane's flush first flex child (same width, no
    // margin/padding before it), so its top-left corner IS the pane's own
    // padding-box top-left — anchoring both at that shared corner instead
    // of their own separate centers makes the ring math (distance from 0,0)
    // line up identically across the boundary.
    image: (opacityPct, scale) => {
      const c = `rgba(var(--pattern-tint-rgb), ${opacityPct / 100})`
      const gap = Math.round(10 * scale)
      return `repeating-radial-gradient(circle at 0 0, ${c} 0, ${c} 1px, transparent 1px, transparent ${gap}px)`
    },
    size: () => 'auto',
  },
}
