# Slate UI

A modern parametric control panel for Grasshopper — an alternative to Human UI, built as a WebView2/Svelte interface instead of native WinForms.

> **Status:** early, actively developed. Expect rough edges. Feedback and bug reports welcome.

## Demo

<table>
<tr>
<td width="50%"><img src="media/01-resize.gif" width="100%"><br>Resizing the panel</td>
<td width="50%"><img src="media/02-edit-mode.gif" width="100%"><br>Entering edit mode</td>
</tr>
<tr>
<td><img src="media/03-split-pane.gif" width="100%"><br>Splitting into panes</td>
<td><img src="media/04-snap.gif" width="100%"><br>Snapping panes into place</td>
</tr>
<tr>
<td><img src="media/05-detach.gif" width="100%"><br>Detaching a tab</td>
<td><img src="media/06-add-to-ui.gif" width="100%"><br>Capturing Grasshopper controls into the panel</td>
</tr>
<tr>
<td><img src="media/07-workspace-and-tabs.gif" width="100%"><br>Workspaces and tabs</td>
<td><img src="media/08-movement-and-group.gif" width="100%"><br>Moving and grouping controls</td>
</tr>
<tr>
<td><img src="media/09-dark-light-mode.gif" width="100%"><br>Dark and light theme</td>
<td><img src="media/10-close-and-delete.gif" width="100%"><br>Closing and deleting tabs/panes</td>
</tr>
<tr>
<td><img src="media/11-edit-mode-overview.gif" width="100%"><br>Edit mode, fully populated</td>
<td></td>
</tr>
</table>

## Why Slate

Grasshopper definitions often need a clean control surface for clients or teammates who shouldn't have to touch the canvas. Slate captures your existing sliders, toggles, buttons, panels and other controls into a floating panel with:

- **Multi-panel layout** — split the window into resizable panes (drag from a corner), each with its own tabs
- **Multiple workspaces** — separate tab sets per workspace, saved per `.gh` file, quick-switch with `Ctrl+1–9`
- **Groups** — collapsible, nestable, with a one-click "capture selected" button
- **Drag-and-drop** everywhere — reorder, move between tabs, move between panes, drop into groups
- **Undo** (`Ctrl+Z`) with a configurable history depth
- **Dark and light themes**, plus per-tab color tagging
- **Custom HSLA/RGBA color picker** with 8-digit hex (`#RRGGBBAA`) support
- Wide control coverage: sliders, boolean toggles, buttons, value lists (dropdown/checklist/sequence), text panels, item pickers, colour swatches, and select controls from the Human and Pancake plugins (read via reflection — no compile-time dependency on either)

State (layout, tabs, groups, captured controls) is saved with your `.gh` file, so a saved definition reopens with its panel exactly as you left it.

## Requirements

- Rhino 8, Windows
- **Windows only.** Slate embeds a WebView2 (Chromium) control via WinForms — there is currently no macOS build and none is planned unless the underlying dependency changes.

## Installation

<!-- TODO: once published — `_PackageManager` search "Slate" (Yak), or Food4Rhino link -->

Not yet published to a package manager. For now, build from source (below) and copy the resulting `Slate.gha` into your Grasshopper `Libraries` folder.

## Building from source

```powershell
# 1. Build the Svelte UI (embedded into the .gha as a resource)
cd ui
npm install
npm run build

# 2. Build the plugin (Rhino must be closed)
cd ../src
dotnet build Slate.csproj
```

Or run `.\build.ps1` from the repo root, which does both steps, closes/reopens Rhino, and deploys the built `.gha` to `%APPDATA%\Grasshopper\Libraries` automatically.

## License

[MIT](LICENSE). Third-party notices (icons) in [NOTICE.md](NOTICE.md).
