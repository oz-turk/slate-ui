# Third-party notices

Slate embeds a small number of SVG icons from **Lucide** (https://lucide.dev),
used under the ISC License. All other icons in the UI (drag handles, corner
handles, etc.) are original.

Icons used:
- `pin` — window pin/unpin toggle (`ui/src/lib/EditToolbar.svelte`), used as-is
- `pipette` — eyedropper button in the custom colour picker
  (`ui/src/lib/ColourPickerPopup.svelte`), used as-is
- "mouse-left" — status bar shortcut hint (`ui/src/lib/StatusBar.svelte`), a
  custom composite: body shape based on Lucide's `mouse` icon, with an added
  filled rect highlighting the left button (Lucide has no dedicated
  left-click icon)
- `lock` / `lock-open` — Trigger's target-lock toggle
  (`ui/src/lib/TriggerRow.svelte`), used as-is
- `refresh-cw` — Trigger's manual/cyclic mode toggle
  (`ui/src/lib/TriggerRow.svelte`), used as-is
- `play` — Trigger's fire button (`ui/src/lib/TriggerRow.svelte`), used as-is
- `link-2` — geometry param's "live Rhino reference" state
  (`ui/src/lib/GeometryParamRow.svelte`), used as-is
- "lock-pin" — geometry param's "internalized" state
  (`ui/src/lib/GeometryParamRow.svelte`), a custom composite: Lucide's `lock`
  icon body with an added keyhole slot (Lucide has no dedicated
  baked/internalized-geometry icon)
- `x` — geometry param's "Clear captured values" button
  (`ui/src/lib/GeometryParamRow.svelte`), used as-is
- `egg-fried` — geometry param's "Bake to Rhino" button
  (`ui/src/lib/GeometryParamRow.svelte`), used as-is, standing in for "bake"
  (Lucide has no dedicated bake icon)

## Lucide — ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part
of Feather (MIT). All other copyright (c) for Lucide are held by Lucide
Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.
