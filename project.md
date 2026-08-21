# DriftDeck — project tracking

## Current state

`main` is at version 0.3.0. CI is green with zero compiler warnings and zero workflow warnings.

**Nothing since v0.2.0 has been run.** The three Tier 2 and Tier 3 batches — roughly 2100 lines
across 22 files — were written on macOS against a `net10.0-windows` WPF target and have only ever
been compiled, never executed. Compiling is not running. That gap is the reason the release is
still unpublished, and it is the first thing on the list below.

There are also no published releases and no tags at all, which means the releases page the README
points at is empty and `UpdateService` has nothing to compare against.

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
| Duplicate panel | `Ctrl+D` |
| Lock / unlock panel | `Ctrl+Shift+L` |
| Mute / unmute panel | `Ctrl+Shift+M` |
| Mute / unmute every browser panel | `Ctrl+Shift+A` |
| Load quick layout 1-9 | `Ctrl+Alt+1` … `Ctrl+Alt+9` (configurable) |
| Content scale | `Ctrl+±` |
| Back / forward | `Alt+←` / `Alt+→` |
| Free placement while dragging | hold `Alt` |

## Build outputs

`artifacts\` holds a single self-contained portable build published from the current source:

```
scripts\Build-Portable.ps1 -Configuration Release
```

`bin\` and `obj\` regenerate on the next build. All three are covered by `.gitignore`.

Browser profile data no longer lands beside the executable: `Services/BrowserEnvironment.cs`
pins the WebView2 user-data folder to `%LOCALAPPDATA%\DriftDeck\webview2`. An older build may
still have left a `DriftDeck.exe.WebView2\` folder next to `DriftDeck.exe`; it is safe to delete.

### Exclusive fullscreen, display recovery, shared browser profile, panel comfort (Tier 2)

- **Exclusive-fullscreen notice.** `Services/FullscreenProbe.cs` polls
  `SHQueryUserNotificationState` every two seconds and reports the one transition that matters:
  a Direct3D application presenting exclusively, which no ordinary topmost window can be drawn
  over. It is the same shell question Windows itself asks before showing a toast — it names no
  process, opens no handle, and installs no hook — so it stays inside the safety policy while
  turning "the overlay is broken" into "switch that game to borderless". Reported once per
  transition in the status strip and the tray; switchable off under Settings.

- **Display recovery.** `Services/DisplayWatcher.cs` subscribes to `DisplaySettingsChanged`,
  `PowerModeChanged` (resume only), and `SessionSwitch` (unlock, console connect), debounced by
  600 ms because a docking change arrives as one notification per monitor and a resolution change
  as several in a row. `PanelWindow` also handles `DpiChanged`. On each settled change the dock
  and every panel are re-clamped into a real work area and the layout is saved — the save is the
  point, since without it the next launch restores the coordinates just found to be unreachable.
  `SystemEvents` holds a process-wide static subscription list and fires on its own thread, so
  handlers marshal to the dispatcher and are removed on dispose.

- **One shared WebView2 environment.** Each panel previously called a bare
  `EnsureCoreWebView2Async()`, which let WebView2 pick its own defaults: a profile folder created
  next to the executable (so a *portable* folder grew state at runtime) and no sharing between
  panels. `Services/BrowserEnvironment.cs` creates one environment, gated by a semaphore because
  panels load concurrently, rooted at `%LOCALAPPDATA%\DriftDeck\webview2`. Panels now share
  logins and cookies and reuse browser processes instead of one group per panel.

- **Idle dimming.** Off by default, because an always-on-top window changing its own opacity
  unasked is startling. One shared dispatcher tick drives every panel rather than a timer each.
  Two exemptions carry the feature: the active panel, and any panel the pointer is over — browser
  content lives in a composition surface and raises no WPF mouse events, so `Services/CursorProbe.cs`
  asks Windows where the cursor is instead, otherwise a page being read would fade out from under
  the reader. The dim is a third multiplier in `PanelHost.ApplyEffectiveOpacity`, so it never
  overwrites the per-panel or overlay-wide values; clearing it restores exactly what the user set.
  Animated dims use `Motion.Hold`, and direct assignments clear the clock first — a holding
  animation outranks a property set, so without that a dimmed panel would ignore its own slider.

- **Panel lock.** `PanelDefinition.IsLocked` refuses drags and takes resizing away at the window
  level, so the chrome stops offering a grip that would be refused. Everything else stays
  available: the accident being prevented is a stray drag during a game, not interaction.

- **Panel duplication and mute.** `PanelDefinition.Clone()` deliberately does not carry the id or
  the lock. Mute uses `CoreWebView2.IsMuted`, which silences without pausing — the right behaviour
  for a video parked beside a game. Per-panel mute is persisted; the dock button mutes everything
  audible in one press, because the reason to reach for mute is usually not yet knowing which
  panel started making noise.

- **Layout export and import.** `Services/LayoutBundle.cs` writes every layout to one
  `.driftdeck` file. Import adds rather than replaces: a name collision becomes
  `Name (imported)`, since silently overwriting a layout someone spent weeks on is not a
  recoverable mistake. The Settings rule pickers bind to an `ObservableCollection`, so freshly
  imported names are selectable without reopening the dialog.

### Startup, quick layouts, hidden-panel suspend, log rotation (Tier 3)

- **Pause hidden browser panels.** Hiding the overlay hid the windows and nothing else: every
  WebView2 kept rendering, decoding video, and running page timers, spending GPU and CPU during
  exactly the moment the user hid the overlay to get them back. `PanelHost.SuspendContentAsync`
  collapses the control — WebView2 refuses to suspend visible content — then calls
  `CoreWebView2.TrySuspendAsync`; `ResumeContent` runs before the windows come back up so a page
  is already live when it appears. A panel that is audibly playing and not muted is skipped:
  music behind a game is a reason to hide the overlay, not to silence it. Suspension is fire and
  forget, because a hidden overlay must not wait on a browser. Switchable off for pages that need
  a live connection.

- **Start with Windows.** `Services/StartupRegistration.cs` writes the per-user `Run` key rather
  than a Startup-folder shortcut, which would need COM shell interop, and never a machine-wide
  key, which would need elevation for a portable folder owned by one user. The registry is the
  truth rather than a copy in settings.json, because the user can disable the entry from Task
  Manager and a stored duplicate would then disagree with reality — `IsEnabled` therefore means
  "registered", not "will definitely run". A moved or renamed folder leaves a stale entry that
  launches nothing, so `RefreshIfStale` repoints it on the next launch.

- **Quick layouts.** `Ctrl+Alt+1` to `Ctrl+Alt+9`, assigned in Settings. Rules already cover
  switching that should happen by itself; this covers deliberately wanting a different workspace
  now, which otherwise means clicking the dock and taking focus off a fullscreen game. The slot
  is stored in `Models/QuickLayout.cs` rather than derived from sorted layout names, which would
  silently remap every shortcut the moment a layout was added. `GlobalHotkeyService` collects
  refusals into `RejectedQuickSlots` instead of throwing: a `Ctrl+Alt+4` already owned by another
  application must not cost the user the pass-through shortcut, which is the one hotkey the
  overlay cannot work without. Loading this way takes the same manual-override hold as the Load
  button. Duplicate digits are refused at save time, since the second registration would fail
  while the row still read as working.

- **Crash log rotation.** `WriteCrashReport` appended to one file per day with no ceiling and no
  pruning, so a crash loop could write without bound and daily files accumulated forever. Files
  now roll at 1 MB to `crash-<date>.<n>.log` — rolling rather than truncating, because the first
  fault of a loop is usually the informative one — and the newest fourteen are kept.

## In progress

Nothing. Three pull requests merged, none open.

## Next up

### Blocking the first release

1. **Smoke-test a build.** Owner action; the assistant cannot run WPF. Grab the portable ZIP from
   the last passing CI run, or `.\scripts\Build-Portable.ps1`. Watch, in order of how likely each
   is to be wrong:
   - hide and restore the overlay — `TrySuspendAsync` has a visibility precondition, and
     collapsing the control to satisfy it is the least certain call in the batch
   - `Ctrl+Shift+M` against `Ctrl+M`, to confirm WPF input-binding precedence
   - the dock at its new 990 minimum width, on the smallest display in use
   - the Settings window with three sections added — it is `SizeToContent="Height"` under a
     `MaxHeight`, so it should scroll rather than clip
   - unplug a monitor with panels on it, and resume from sleep

2. **Tag v0.3.0.** Fill the date into `CHANGELOG.md`, then `git tag v0.3.0` and push it. This is
   the first execution of the `release` job and of `download-artifact@v8`, neither of which has
   ever run — the job is gated on `refs/tags/v*` and reports as skipping on every build so far.
   Expect to watch it.

### Features, in the order they are worth doing

3. **Gather every panel onto the current monitor.** Display recovery covers a monitor
   disappearing; nothing covers a panel dragged somewhere the user cannot find. Reuses the
   clamping and cascade logic already in `MainWindow`, so it is small.

4. **Notes export and clipboard copy.** Notes live only inside layout JSON. There is no way to get
   them out, which makes the app a place data goes in and does not leave.

5. **Bookmarks and recent URLs per layout.** Typing an address into an 18-pixel toolbar during a
   game is the worst interaction left in the product.

6. **Tray panel list.** The tray menu is four fixed items. Listing open panels gives a way to
   reach one without the dock.

7. **Timer and checklist panel types.** Of the four proposed panel types these are the two that
   pair with the actual use case — cooldowns and quest steps.

8. **Keyboard accessibility.** Tab traversal across the dock and panels, and
   `AutomationProperties` on the controls that still lack them.

### Parked, with a reason

- **Tests** for the pure services (`Snap`, `HotkeyGesture`, `LayoutRule`, `LayoutStore`,
  `LayoutBundle`, `QuickLayout`) — owner is handling this. They were written as pure functions
  precisely so this is cheap.
- **Code signing**, once a certificate exists. Azure Trusted Signing is the cheapest route that
  works from GitHub Actions. Note this gates a *good* first release rather than any release:
  SmartScreen warns on every download until it exists.
- **Installer or `winget` package**, so an available update is not a manual ZIP swap.
- **Close with the host application.** Needs an explicit decision on the standing policy that
  DriftDeck never asks whether a game is running. That is a deliberate change of stance, not a
  feature to slip in.
