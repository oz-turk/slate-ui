// #RRGGBBAA (8-digit) is the wire format everywhere — falls back to opaque
// when only 6 digits are present (older captured state, etc).
export function hex8ToRgba(hex) {
  hex = hex.replace('#', '')
  if (hex.length === 6) hex += 'ff'
  return {
    r: parseInt(hex.slice(0, 2), 16),
    g: parseInt(hex.slice(2, 4), 16),
    b: parseInt(hex.slice(4, 6), 16),
    a: parseInt(hex.slice(6, 8), 16),
  }
}

export function rgbaToHex8({ r, g, b, a }) {
  const h = n => Math.round(Math.max(0, Math.min(255, n))).toString(16).padStart(2, '0')
  return '#' + h(r) + h(g) + h(b) + h(a)
}

// Source-over composite of an #RRGGBBAA colour against an opaque #RRGGBB
// backdrop, returned as an opaque #RRGGBB. Used where a translucent colour
// (e.g. a pane's own background override) needs an equivalent SOLID colour
// — a knockout outline, for instance, can't itself be translucent (it would
// let whatever's behind bleed straight through, defeating the knockout), so
// it needs the colour that translucent fill actually *reads as* once
// blended with its backdrop, not just its alpha-stripped hue (which can be
// far more saturated/different than what's visually on screen at low alpha).
export function compositeOverOpaque(hex8, baseHex) {
  const { r, g, b, a } = hex8ToRgba(hex8)
  const base = hex8ToRgba(baseHex)
  const t = a / 255
  return rgbaToHex8({
    r: r * t + base.r * (1 - t),
    g: g * t + base.g * (1 - t),
    b: b * t + base.b * (1 - t),
    a: 255,
  })
}

export function rgbToHsl(r, g, b) {
  r /= 255; g /= 255; b /= 255
  const max = Math.max(r, g, b), min = Math.min(r, g, b)
  let h, s
  const l = (max + min) / 2
  if (max === min) {
    h = s = 0
  } else {
    const d = max - min
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
    switch (max) {
      case r: h = (g - b) / d + (g < b ? 6 : 0); break
      case g: h = (b - r) / d + 2; break
      default: h = (r - g) / d + 4; break
    }
    h /= 6
  }
  return { h: h * 360, s: s * 100, l: l * 100 }
}

export function hslToRgb(h, s, l) {
  h = ((h % 360) + 360) % 360 / 360
  s /= 100; l /= 100
  let r, g, b
  if (s === 0) {
    r = g = b = l
  } else {
    const hue2rgb = (p, q, t) => {
      if (t < 0) t += 1
      if (t > 1) t -= 1
      if (t < 1 / 6) return p + (q - p) * 6 * t
      if (t < 1 / 2) return q
      if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6
      return p
    }
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s
    const p = 2 * l - q
    r = hue2rgb(p, q, h + 1 / 3)
    g = hue2rgb(p, q, h)
    b = hue2rgb(p, q, h - 1 / 3)
  }
  return { r: r * 255, g: g * 255, b: b * 255 }
}
