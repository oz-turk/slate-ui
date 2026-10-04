import { cubicOut } from 'svelte/easing'

// Translate-only replacement for svelte/animate's `flip`.
//
// Svelte 5's own flip also animates a `scale(sx, sy)` of from.size / to.size
// (see node_modules/svelte/src/animate/index.js). That's right for a box that
// really resizes, but for our row slots it meant a row whose HEIGHT changed
// (collapsing the Path Mapper's Graph: ~450px → ~100px) was drawn stretched
// vertically by the old/new height ratio and relaxed back over 150ms —
// glyphs visibly squashed/stretched (2026-10-04 kullanıcı: Graph aç/kapa
// yapınca "titreme", ekran kaydıyla yakalandı: içerik ~2.5x dikey gerilip
// normale dönüyordu).
//
// Here only the TOP-LEFT corner's movement is animated: a row whose own size
// changes but whose top edge stays put doesn't animate at all, and rows
// below it still slide smoothly into their new position.
export function flipTranslate(node, { from, to }, params = {}) {
  const { delay = 0, duration = d => Math.sqrt(d) * 120, easing = cubicOut } = params

  const style = getComputedStyle(node)
  const transform = style.transform === 'none' ? '' : style.transform

  const dx = from.left - to.left
  const dy = from.top - to.top

  return {
    delay,
    duration: typeof duration === 'function' ? duration(Math.sqrt(dx * dx + dy * dy)) : duration,
    easing,
    css: (_t, u) => `transform: ${transform} translate(${u * dx}px, ${u * dy}px);`,
  }
}
