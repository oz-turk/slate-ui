// Svelte action shared by every popup/menu that closes itself on an outside
// click or Escape (ContextMenu, SettingsPanel, ColourPickerPopup). Listens in
// the pointerdown CAPTURE phase so it fires before whatever click opened the
// popup — important for a popup opened by the same kind of pointerdown event
// it's now listening for.
//
// params.exclude (optional CSS selector) lets an always-visible toggle button
// live outside the popup's own DOM without the click that reopens it being
// read as an outside click that closes it first.
export function clickOutside(node, { onClose, exclude } = {}) {
  function onPointerDown(e) {
    if (exclude && e.target.closest(exclude)) return
    if (!node.contains(e.target)) onClose()
  }
  function onKeydown(e) {
    if (e.key === 'Escape') onClose()
  }
  window.addEventListener('pointerdown', onPointerDown, true)
  window.addEventListener('keydown', onKeydown)
  return {
    destroy() {
      window.removeEventListener('pointerdown', onPointerDown, true)
      window.removeEventListener('keydown', onKeydown)
    }
  }
}
