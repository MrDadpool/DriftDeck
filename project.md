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

### Per-application layouts, onboarding, recovery, updates (Tier 1)

- **Per-application layouts.** `Services/ForegroundWatcher.cs` polls the foreground window once
  a second and reports the owning process name and window title. It is deliberately the weakest
  mechanism that answers "which application is the user looking at": `GetForegroundWindow`,
  `GetWindowThreadProcessId`, and `GetWindowText`, the same read-only calls the taskbar makes.
  Polling was chosen over `SetWinEventHook` because it needs no cross-process callback and still
  catches title changes, which a foreground-only hook misses.
  `Models/LayoutRule.cs` holds the matching as pure functions, so it is testable without a
  window: a rule matches an executable name, optionally narrowed by a title substring, and
  title-qualified rules outrank bare process rules regardless of list order.
  A match waits 1.2 s and is re-checked before the layout loads, so tabbing past a window does
  not load its layout, and loading a layout by hand suppresses switching until the user moves to
  a different application.
- **First-run tour.** `OnboardingWindow` covers what DriftDeck is, the two global shortcuts, and
  the first panels. A transparent overlay whose only chrome is a thin dock cannot explain
  pass-through by being looked at, and a global shortcut is undiscoverable by clicking. Skipping
  counts as completing it: re-showing it every launch would punish dismissal, and Settings says
  everything it says.
- **Crash recovery.** `Services/SessionSentinel.cs` writes a marker on start and clears it on a
  deliberate exit, so the next launch can tell "the user quit" from "the process died" — an
  overlay usually dies with the game it sits over. The layout was already durable at ~650 ms per
  change, so this is not about restoring data: it reports what happened and writes the fault to
  `%LOCALAPPDATA%\DriftDeck\logs`. Faults are logged and then allowed through; swallowing one
  would leave an always-on-top window alive in an unknown state over whatever the user is doing.
- **Update check.** `Services/UpdateService.cs` makes one anonymous GET of the public GitHub
  release list per launch and reports a newer tag in the status strip and the tray. It never
  installs anything — DriftDeck is a portable folder, so replacing itself is not on the table.
  Release builds stamp the tag into the assembly version (`Build-Portable.ps1 -Version`, wired
  into CI) so a published build can compare against it.
- **Not signed.** Releases carry no Authenticode signature, so SmartScreen warns on first run.
  This is documented in the README rather than worked around.

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

### Tests

`tests\DriftDeck.Tests` covers the four services whose rules are invisible in the running app:
`Snap` (edge beats grid, the 12-unit threshold, away-from-zero midpoints, guide-line
construction), `HotkeyGesture` (parse, round trip, the combinations Windows owns, the
`RegisterHotKey` flag values), `LayoutRule` (case handling, title narrowing, and the guarantee
that a title-qualified rule outranks a bare one regardless of list order), `Checklist` (what
counts as an item, the footer wording, clear-done), `TimerState` (every transition, duration
parsing and formatting, and reading a finished timer hours late), `ImagePin` and `ImageStore`
(accepted files, caption wording, PNG round trip, and the unreferenced sweep), and `LayoutStore`
(round trip, name normalisation, copy-without-switching, delete guards, a corrupt file, and the
version 1 to version 2 panel-position migration).

`LayoutStore` gained a constructor taking a directory so tests read and write a disposable temp
folder instead of the user's real layouts. Nothing else changed to make the code testable.

```
dotnet test DriftDeck.slnx
```

166 tests, all green, and CI runs them between build and packaging.

### Checklist panels

`+ List` on the dock, or `Ctrl+K`. A checklist is a tick box, a line of text, and a footer that
reports what is **left** rather than what is done — the question a checklist exists to answer.

- Rows bind straight to the persisted `ChecklistItem`, which raises change notifications. A
  parallel view model for two fields would be more code than it saves.
- The add box sits under the list, so a new item appears where the cursor already is instead of
  the view jumping to the top. Enter adds and clears the box; Enter or Escape inside a row
  returns to the add box, so a burst of typing never reaches for the mouse.
- The per-row remove button is invisible until the row is hovered or focused. A delete on every
  line reads as clutter on a list whose usual action is ticking a box.
- `Clear done` appears only when something is ticked.
- Blank input is not an item. Overlong text is cut at 200 characters rather than refused, so a
  paste still lands but one line cannot drive the panel's layout.
- `Checklist` in `Models/Checklist.cs` holds those rules as pure functions, tested without a
  window.

Layouts written before checklists existed have no `Items` array; it deserialises to an empty
list, which is covered by a test.

### Timer panels

`+ Timer` on the dock, or `Ctrl+T`. A countdown with a length box, start/pause, and reset.

- **A running timer is the instant it ends, not a tick count.** `PanelDefinition.TimerEndUtc`
  holds that instant, so the remaining time is whatever the clock says — the panel can be
  shaded, the layout saved, or the process killed, and it comes back still telling the truth.
  This is also what keeps the ~650 ms layout save off the per-second path: the timer persists on
  start, pause, reset, and length change, never on a tick. The 250 ms `DispatcherTimer` touches
  the readout and nothing else, and it stops whenever the timer is not running.
- Reaching zero stops the clock and turns the readout to the warning colour. Nothing else
  happens — an always-on-top overlay must not steal focus or make noise over a game.
- Starting a finished timer restarts the full length rather than being a no-op.
- Changing the length stops a running timer: the persisted end instant was derived from the old
  length and no longer means anything.
- The length box reads a bare number as minutes and colons literally, so `5`, `5:00`, `90`, and
  `1:30:00` all work. Rejected input is rewritten to the length still in force rather than left
  sitting in an error state the panel has no room for.
- The readout is the one piece of text outside the five-step type scale. It is what the panel
  exists to show, and it scales with the panel's content scale like everything else.
- `TimerState` in `Models/CountdownTimer.cs` is a record struct with pure transitions, so the
  tests supply their own clock and none of them wait for real time to pass.

### Image panels

`+ Image` on the dock, or `Ctrl+I`. Drop a file on the panel, paste with `Ctrl+V`, or use
**Choose image**.

- **A panel points at a file; it does not copy it.** An image the user picked is their file in
  their folder, and duplicating it into DriftDeck's storage would grow a folder they never asked
  for and never see. The cost is that a pinned image can go missing, which the panel says
  plainly rather than hiding: the path is kept, the caption reads `name — file is missing`, and
  the empty state explains what to do.
- **Pasted images are the exception.** The clipboard hands over pixels with no file behind them,
  so `Services/ImageStore.cs` writes one as PNG under `%LOCALAPPDATA%\DriftDeck\pasted-images`.
  PNG because a paste is usually a screenshot or a diagram, where re-encoding artefacts are the
  whole problem. Pasting a *file* from the clipboard copies nothing — it is treated like a drop.
- Those pasted files are the only ones DriftDeck owns, and nothing else would ever remove one,
  so startup sweeps the folder for images no layout points at. Every layout is read, not just
  the current one, or the sweep would delete an image another layout is still using.
- The bitmap is loaded with `BitmapCacheOption.OnLoad`, so the file is not held open. A pinned
  image the user could not then move or delete would be worse than one that goes missing.
- Fit is uniform and content scale multiplies it, with the scroll viewer supplying panning once
  the image is bigger than the panel — a zoom and a separate fill mode would be two controls
  doing one job.
- The file name becomes the panel title, unless the user has typed one.
- `Models/ImagePin.cs` holds the accepted extensions, the drop-picking, and the caption wording
  as pure functions.

## Keyboard

| Action | Shortcut |
| --- | --- |
| Toggle pass-through | `Ctrl+Alt+O` (configurable) |
| Hide / restore overlay | `Ctrl+Alt+H` (configurable) |
| New browser panel | `Ctrl+B` |
| New notes panel | `Ctrl+N` |
| New checklist panel | `Ctrl+K` |
| New timer panel | `Ctrl+T` |
| New image panel | `Ctrl+I` |
| Paste an image into an image panel | `Ctrl+V` |
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

- Remaining panel type: markdown notes.
- Bookmarks and recent URLs per layout.
- Idle dim for untouched panels.
- Layout export and import as a shareable file.
- Code signing, once a certificate exists. Azure Trusted Signing is the cheapest route that
  works from GitHub Actions.
