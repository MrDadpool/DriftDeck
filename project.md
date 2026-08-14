# DriftDeck — project tracking

## Current state

The overlay shell has been through a full UX and visual pass. The app builds clean and runs on
Windows 11 with .NET 10.

## Completed

### Feedback and shell

- **Status bar made visible.** The dock's status row was declared with `Height="0"`, so every
  status message the app wrote rendered into a zero-height row and was never seen. The dock now
  has a real status strip with a state dot (info / success / warning) and a short fade per
  message.
- **Full control templates.** `App.xaml` defines templates for `Button`, `TextBox`, `ComboBox`,
  `Slider`, `CheckBox`, `ScrollBar`, and `ToolTip`, plus the palette, type scale, and metric
  tokens. Hover, pressed, focus, and disabled states are explicit; no control falls back to
  system chrome on the dark theme.
- **Motion.** Button hover, panel entry, dock collapse/restore, pass-through scrim, and an
  indeterminate browser load bar — three shared durations (120 / 200 / 320 ms) and three shared
  easings, in `App.xaml` for XAML and `Services/Motion.cs` for code.
- **Reduced motion.** `Motion.Enabled` reads `SystemParameters.ClientAreaAnimation`, and
  `App.OnStartup` collapses the duration tokens to zero when animations are off, which disables
  every declarative storyboard in one move. An always-on-top overlay sits over whatever the user
  is actually watching, so unwanted motion matters more here than in an ordinary window.

### Panels

- **Browser panels are browsers.** Back, forward, reload, load progress, typed navigation errors
  with a retry action, and pop-ups kept inside the panel via `NewWindowRequested` — a bare
  WebView2 pop-up window has no chrome and cannot be closed.
- **Panel identity.** Titles follow `CoreWebView2.DocumentTitle`; double-clicking a title renames
  it and pins the custom name. The OS window title tracks the panel title.
- **Raise on click.** Clicking web content or the notes box raises the panel, driven by window
  activation.
- **Resize from every edge** via `WindowChrome`, replacing a single corner grip.
- **See-through is a multiplier.** The dock slider no longer overwrites per-panel opacity;
  effective opacity is `panel × global`.
- **Minimise** rolls a panel up to its title bar rather than to a taskbar the overlay does not
  have. A shaded panel keeps its exact position, so restoring puts the content back where it was.
  Toggle by the title-bar chevron, `Ctrl+M`, or double-clicking the title bar. State persists via
  `PanelDefinition.IsCollapsed` / `RestoreHeight`.

### Window ordering

Every DriftDeck window is topmost, so Windows ordered them among themselves by activation alone
and nothing could deliberately lift a panel. `Services/WindowOrder.cs` wraps
`SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`, which reorders without the flash a `Topmost` toggle
causes and without pulling focus off the application underneath. Wired to panel activation,
creation, reopen, and the dock's own activation. `Ctrl+Tab` / `Ctrl+Shift+Tab` cycle panels.

### Placement

`Services/Snap.cs` holds the placement math as pure functions: an edge lands on a neighbouring
edge when it is within 12 units, otherwise it falls onto an 8-unit grid. Guides are the current
monitor's work area plus every other panel and the dock. Holding **Alt** disables both. Resizing
snaps too — `PanelWindow` handles `WM_SIZING` and quantises the dragged edge in device pixels,
which window-chrome resizing previously bypassed entirely.

### Layouts and settings

- Press-to-record hotkey capture in Settings, with per-field reset.
- `Copy` writes a duplicate and leaves you on the layout you were editing.
- `Delete` is a two-step inline confirm. The modal dialog it replaced could pull focus out of a
  fullscreen game — exactly what an overlay must not do.
- The dock is horizontally resizable and clamps to the monitor's work area.
- New panels clamp into view and cascade so they never land exactly on the last one.
- Tray icon with show/hide, pass-through, settings, and quit.
- Reopen the last closed panel with `Ctrl+Shift+T`.

### Visual system

- Every colour resolves through a named token. No XAML in the app carries a raw hex value. The
  one documented exception is `WebView2.DefaultBackgroundColor`, a GDI colour converted from the
  `SurfaceDeep` token in code and forced opaque, since WebView2 rejects partial alpha.
- The accent signals only state the user can act on: active panel, focus, checked, success, mode,
  primary action.
- Three button tiers — accent for the two buttons that create a panel, ghost for layout
  housekeeping, danger for delete.
- Sentence case throughout; small caps kept on two group labels as a deliberate device.
- One icon family (Segoe MDL2 Assets) at one size.
- Contrast: the safety disclaimer was 2.9:1 and the notes placeholder 3.3:1. Both are now 7:1.
- Type scale of five steps with an 11 px floor.

### Density

Bars were too tall. A literal halving would have put panel title bars at 14 px and their buttons
at 10 px — under what can be reliably clicked — so the pass went as far as stays usable.

| | before | after |
| --- | --- | --- |
| Panel title bar | 28 px | 18 px |
| Panel title buttons | 24 x 20 | 20 x 16 |
| Shaded panel height | 30 px | 20 px |
| Dock title strip | 24 px | 18 px |
| Dock toolbar | 48 px | 34 px |
| Dock status strip | 22 px | 16 px |
| **Dock total** | **94 px** | **68 px** |
| Control height | 28 px | 24 px |
| Icon button | 26 px | 22 px |

Every change went through the shared tokens in `App.xaml`, so density stays a single place to
tune. The type scale was left alone — 11 px is already the floor, and shrinking text is what
makes a compact UI unusable rather than dense.

## Keyboard

| Action | Shortcut |
| --- | --- |
| Toggle pass-through | `Ctrl+Alt+O` (configurable) |
| Hide / restore overlay | `Ctrl+Alt+H` (configurable) |
| New browser panel | `Ctrl+B` |
| New notes panel | `Ctrl+N` |
| Reopen last closed panel | `Ctrl+Shift+T` |
| Save layout | `Ctrl+S` |
| Cycle panels | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| Roll panel up / down | `Ctrl+M` |
| Focus address bar | `Ctrl+L` |
| Reload page | `Ctrl+R` |
| Close panel | `Ctrl+W` |
| Content scale | `Ctrl+±` |
| Back / forward | `Alt+←` / `Alt+→` |
| Free placement while dragging | hold `Alt` |

## Build outputs

`artifacts\` holds a single self-contained portable build published from the current source:

```
scripts\Build-Portable.ps1 -Configuration Release
```

`bin\` and `obj\` regenerate on the next build. All three are covered by `.gitignore`.

If a run of the portable exe leaves a `DriftDeck.exe.WebView2\` folder beside it, that is
per-user browser profile data created at runtime, not part of the build. Delete it before
redistributing the folder.

## Next up

- Nothing outstanding.
