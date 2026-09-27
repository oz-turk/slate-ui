# Changelog

Notable user-facing changes, newest first. Internal refactors and pure repo maintenance are left out; see `git log` for the full history. Version numbers are derived automatically from the build (csproj + git hash), so entries are grouped by date instead of a manually-bumped version.

## Unreleased

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
