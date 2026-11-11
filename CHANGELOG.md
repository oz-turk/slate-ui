# Changelog

Notable user-facing changes, newest first. Internal refactors and pure repo maintenance are left out; see `git log` for the full history. Version numbers are derived automatically from the build (csproj + git hash), so entries are grouped by date instead of a manually-bumped version.

## Unreleased

## 0.4.1 - 2026-11-11 (hotfix)

- Fix Rhino 9 closing as soon as a viewport tab was drawn. The Rhino function the viewport tab uses takes different arguments in Rhino 9 than in Rhino 7 and 8; Slate now checks which form the running Rhino has, and falls back to the slower drawing path if it finds neither.

## 0.4.0 - 2026-11-11

- **Viewport tab:** right-click a pane and choose Viewport Tab to turn it into a live Rhino viewport, with its own camera, view and display mode menus, grid and axes switches, and a Locked option. Right-drag orbits, Shift+right-drag pans, Ctrl+right-drag dollies, and the wheel zooms (optionally at the cursor). The camera and settings are saved with the .gh.
- **Focus:** Custom Preview rows get a Focus button. Clicking it frames the focused previews in every open viewport tab, and orbiting turns around them.
- **Capture Grasshopper groups:** capturing a selected group creates a Slate group with the same name and colour, with its members inside. Nested groups come along.
- **Startup splash:** the window shows the Slate logo in your theme and accent colour while a file loads, instead of staying black.
- **Status bar readouts:** the last solve time and how long ago the file was saved, with a dot when there are unsaved changes.
- The Scenes menu shows a spinner while an applied scene is still solving.
- Click the number next to a popup slider (colour channels, pattern scale and opacity, font size) to type a value.
- Context menus can hold on/off items, and checkboxes now look the same everywhere.
- Changes made in Slate (captures, layout edits, values, undo, scenes) now mark the Grasshopper file as modified, so closing it asks to save them.
- The Bake dialog opens centred on the Rhino window and in front of Slate, and one Ctrl+Z undoes the bake.

Known limitation: the viewport tab relies on an undocumented Rhino function to draw directly into the Slate window. If that function isn't available it falls back to a slower drawing path. It has only been tested on Rhino 8.

## 0.3.0 - 2026-11-03

- **Scenes:** save named snapshots of control values from the top toolbar, then apply, overwrite, rename or delete them. Scenes are saved with the .gh. A dot next to the active scene shows when live values no longer match it, and an optional "Show changes" switch marks each control that differs. Covers sliders, toggles, single-select value lists / item pickers / item selectors, colour pickers, MD Sliders and typed-input text panels.
- **Path Mapper capture:** shows the output tree as a radial graph. Drag a wedge along its ring to reorder sibling branches (children move with it). A Source/Target rule editor in Edit mode writes your own rules.
- **MD Slider capture:** an XY pad that uses the slider's own domains, with the X/Y readout under the name.
- **Graph Mapper capture:** pick any graph type Grasshopper knows (native or plug-in) from the row's right-click menu. Drag control points (shown on hover) and hold Ctrl to snap to the ruler. The curve matches the one drawn on the canvas.
- **Custom Preview capture:** one row with Preview, Render, Bake and Enabled buttons plus a viewport filter. Bake opens Grasshopper's own Bake dialog.
- **Show icons per type:** a settings page to choose which object types show their icon, grouped as Geometry / Primitive / Input / Other. Press and drag across tiles to toggle several at once.
- **Accent colour:** Blue, Orange, Red, Green, Purple or Mono, each with dark and light variants. The window icon follows the accent.
- **Save from Slate:** a Save button in the top toolbar and Ctrl+S (while the Slate window is active) save the Grasshopper file. A file that was never saved opens Grasshopper's Save As dialog.
- Drag tabs and workspace tabs to reorder them. Dropping on another pane's tab bar adds the tab there instead of splitting the pane.
- Dropping below an expanded group's last row now places the item after the group.
- New "Show Tab Bar" option in the pane context menu.
- Header-style text panels now cut off with an ellipsis instead of overflowing.
- Tab toggles Edit mode everywhere, including inside text fields.
- The context menu scrolls when it is taller than the window.
- Fix sliders snapping back to an older value mid-drag (since 0.2.2, most visible on slow definitions). Alt-drag copies keep updating while you drag.
- Fix undo jumping to a different workspace. Switching workspaces is now an undo step of its own, like switching tabs.
- Undo now keeps 50 steps by default instead of 10.
- Fix rows looking stretched for a moment after their height changed. Remove buttons are now the same size and line up across rows.

## 0.2.4 - 2026-10-01

- **Show icons:** new Settings toggle (off by default) shows Grasshopper's own icon next to each captured row's name. In preview mode, clicking the icon or double clicking the name jumps the canvas to the original object and selects it.
- **Group header appearance menu:** each group header has its own menu (⋮ button in edit mode, or right click) for a background tint and label alignment, colour, size, bold, italic and underline. The pane background popup now uses the same pastel palette and lays its patterns out in two columns.
- Capture and Sort: Canvas Position now read the canvas in columns, left to right, top to bottom within each, so aligned stacks keep their order. Window capture of mixed selections is one interleaved pass instead of batching by type.
- Fix undo only changing Slate's own copy: slider, toggle, panel and colour values changed by undo are now sent back to the Grasshopper objects.
- Fix the x shortcut deleting a whole group when the cursor was anywhere over its body. It now only works on the group header.
- Dragging a slider onto the top or bottom of a group header places it beside the group; the middle still nests it.
- Fix preview pins not showing for recently captured widgets, and keep slider ranges and nested-group controls in sync when restoring.

## 0.2.3 - 2026-09-27 (hotfix)

- Fix the right-click context menu not opening on several row types (Button, Colour Picker, Data Dam, Geometry Param, Panel, Param Viewer, Toggle, Trigger, Value List).
- Fix Alt-drag not detecting Alt on those same row types, so copying a control instead of moving it actually worked.
- Fix the colour picker's saturation track reading flat at extreme lightness.
- Fix the pane background knockout colour reading more saturated than its real on-screen blend at low alpha, and a halo it introduced on rows with no pane pattern set.
- Seed a new Custom pane colour with the app's accent hue instead of near-black, and apply it immediately on first click.

## 0.2.2 - 2026-09-27

- **Alt-drag to copy a control:** Alt-drag a slider/toggle/button row to spawn a synced copy instead of moving the original — copies share the underlying GH object and stay in sync on value/name changes. Alt+X (or a row's right-click menu) removes every copy at once.
- New Settings toggle to allow capturing the same GH object twice from the canvas (off by default).
- Fix Param Viewer fullscreen: settings gear only shows in edit mode, size stepper is locked there (fullscreen's size comes from the window, not the stored preview size), settings popup now paints above the fullscreen overlay, and the item-count toggle works again for captured/copied controls.
- Settings panel shortcuts list shows the ~10 most useful shortcuts by default, with a "Show more" toggle for the rest.
- Icon consistency pass across Param Viewer/Text Panel rows; fix sunburst crowding and single-branch rendering.
- Data Dam and Param Viewer native captures.
- Generalize Text Panel appearance settings to all panels.
- Show the open file's name in the Slate window title.
- Ctrl+scroll zoom hint in the status bar.
- Per-pane background colour/pattern and Obsidian-style tab colouring.
- Buy Me a Coffee badge and GitHub sponsor button.

## 0.2.1 - 2026-09-21 (hotfix)

- Fix pinned preview geometry clipping in perspective viewports (Top and other parallel views were unaffected) — the pin conduit now contributes its objects' bounds to Rhino's per-frame clipping-plane calculation.
- Geometry param capture now allows a wired param too, with Set/Pick disabled while it's wired; Internalize disconnects the source (native GH behaviour) and re-enables Set/Pick.
- Fix wired geometry param captures being dropped on file save/reopen.
- Fix the group-level preview-pin eye icon lighting up when the group was hidden instead of when it was visible.

## 0.2.0 - 2026-09-21

- Fix Aero Snap windows not landing flush against a screen edge/corner, and window resize occasionally squeezing the nearest pane after a snap or similar OS-driven window jump.
- Sort by canvas position now reorders a group's own contents too, not just the top level.
- Pin/unpin toggle uses distinct eye/eye-off icons instead of a colour swap.
- Value column now fits large numbers without truncating.
- Live-sync Value List items on solve; fix checklist header squish on resize.
- Markdown-style header lines in checklist-mode Value Lists, for grouping items visually.
- Checklist headers collapse when unnamed instead of wasting vertical space; ctrl/shift-click now selects a whole group at once.
- **Preview pin:** force any captured geometry param, or a whole group, to always draw on the GH canvas regardless of GH's own preview filter/mode.
- Fix title bar defaulting to dark on a document with no saved theme.
- **Geometry param capture:** capture freestanding Point/Curve/Brep/Mesh/Surface/SubD/Box/Geometry params (plus several Rhino 8-only types) with Set/Clear/Internalize/Bake.
- Fix maximize state corrupting on document close/switch; reduce reapply flicker.
- Rescale saved pane sizes when a file is reopened on a different-resolution screen; persist maximized state across a cross-resolution reopen.
- Fix window not restoring maximized when it was saved that way.
- Fix the file staying marked dirty right after every save and open.
- **Trigger capture:** capture the native Trigger (GH_Timer) component as a panel row with interval, mode, lock-targets and a manual fire button.
- Fix zoom hint hidden behind Ctrl shortcuts; status bar now fades text that overflows instead of clipping it.

## 0.1.4 and earlier

Not tracked here; see `git log`.
