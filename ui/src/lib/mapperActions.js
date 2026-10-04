import { applyReorderToOrders } from './treeLayout.js'
import { postToCs } from './ipc.js'

// A drop in the graph (DataTreeSunburst 'reorder') → the full persisted
// reorder state. Returns the new `orders` for the host to store (row:
// patchSlider, fullscreen: syncControl) and sends the same whole state to C#,
// which regenerates the mapper's Lexers from scratch (never incremental
// diffs). Shared so the row and the fullscreen view can't drift apart.
export function commitReorder(slider, detail) {
  const { parentPath, from, to } = detail
  const orders = applyReorderToOrders(slider.mapperOrders ?? {}, slider.tree, parentPath, from, to)
  postToCs({ type: 'pathMapper_apply', id: slider.sourceId ?? slider.id, orders })
  return orders
}
