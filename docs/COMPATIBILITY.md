# Windows compatibility

## Requirements

- Windows 10 or Windows 11, 64-bit.
- The .NET 10 Desktop Runtime (or the .NET 10 SDK for building). A self-contained build bundles the runtime instead.
- The Windows versions Quire supports are the ones .NET 10 supports; check Microsoft's .NET 10 supported OS list before promising support for an older Windows 10 release.

## Test status

| Windows | Build | Status |
|---|---|---|
| Windows 10 IoT Enterprise LTSC 2021 (21H2) | 19044 | Built and run. Light and dark themes, Markdown and code previews, animated GIFs, the recovery prompt and PDF output were checked on screen; all four exports were generated from the sample documents. |
| Windows 11 | — | Not tested yet. Use the checklist below. |

Automated tests (`dotnet test Quire.slnx`) cover storage, undo, recovery, search, settings and every exporter. They do not depend on the Windows version.

## Behaviour that depends on the Windows version

| Area | Windows 10 | Windows 11 | Notes |
|---|---|---|---|
| Dark title bar | 1809 and later | Yes | Set with `DWMWA_USE_IMMERSIVE_DARK_MODE` (attribute 20, or 19 before 20H1). Older builds keep a light title bar. |
| "Use Windows setting" theme | Reads the app mode from Settings > Personalization > Colors | Same | Changes apply live. |
| High contrast | Yes | Yes (contrast themes) | Overrides the light and dark palettes with the user's system colors. |
| Window corners | Square | Rounded by Windows | The window frame is left to Windows. Everything Quire draws inside it stays square. |
| Menu and toolbar icons | Segoe MDL2 Assets | Segoe MDL2 Assets (Segoe Fluent Icons also present) | MDL2 exists on both, so one font serves both. |
| Code font | Cascadia Mono if installed, otherwise Consolas | Cascadia Mono ships with Windows Terminal | The fallback order is set in `Themes/Document.xaml`. |
| Menus, scroll bars, tooltips | Quire templates | Quire templates | Drawn by Quire so they match light and dark. Dialogs (open, save, message boxes) stay native. |

## Windows 11 checklist

1. Launch, then open `sample-docs/quire/sample.quire` and `preview-demo.quire`.
2. Switch View > Theme between Light, Dark and Use Windows setting. Change the Windows app mode while Quire is open.
3. Turn on a contrast theme (Settings > Accessibility > Contrast themes).
4. Check the title bar colour, menus, context menus, scroll bars and find bar (Ctrl+F) in each theme.
5. Paste a screenshot (Win+Shift+S, then Ctrl+V) and drag a file in from Explorer.
6. Export to all four formats and open each one.
7. Kill Quire from Task Manager with unsaved changes, relaunch, and restore the draft.
8. Scale the display to 150% and confirm images and text stay sharp.
