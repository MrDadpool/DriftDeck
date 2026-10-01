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

### Visual refresh (Codex, accepted 2026-10-01)

Supersedes the density table above. Roomier controls, a cyan accent (`#72DBED`) replacing teal,
darker blue surfaces, lighter muted/disabled text, and a `ConsoleHeaderBrush` gradient on the
dock title strip. The dock toolbar is now two rows: create buttons and fade on top, layout
controls below.

| | density pass | refresh |
| --- | --- | --- |
| Panel title bar | 18 px | 30 px |
| Panel title buttons | 20 x 16 | 26 x 26 (24 wide in panels) |
| Shaded panel height | 20 px | 32 px |
| Dock title strip | 18 px | 30 px |
| Dock status strip | 16 px | 24 px |
| **Dock total** | **68 px** | **146 px** |
| Dock min width | 1016 px | 780 px |
| Collapsed dock | 250 x 18 | 340 x 30 |
| Control height | 24 px | 32 px |
| Icon button | 22 px | 28 px |

`scripts/check-ui.py` statically checks resource keys, event handlers, and that the dock and
shaded-panel constants in code match the XAML. Run it after any metric change.

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
| Duplicate panel | `Ctrl+D` |
| Lock / unlock panel | `Ctrl+Shift+L` |
| Mute / unmute panel | `Ctrl+Shift+M` |
| Mute / unmute every browser panel | `Ctrl+Shift+A` |
| Copy the focused notes panel | `Ctrl+Shift+C` (notes panels only) |
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

### Gather, notes export, bookmarks (Tier 4)

- **Gather panels onto the current monitor.** `Services/Gather.cs` is the placement math as a
  pure function, like `Snap` and `LayoutRule`: given a work area and the panel sizes, it returns
  positions, cascading by 28 within a column and wrapping to a new column at the bottom edge.
  A cascade rather than a tile, because tiling would have to resize panels and overlapping title
  bars still stay individually clickable. The dock's own band is reserved only when the dock is
  on that monitor and in its upper half. Locked panels are moved: the lock refuses accidental
  drags, and a locked panel stranded off-screen is the exact case the command exists for.
  Reached by one dock icon button — deliberately no shortcut, since the dock is what the user
  still has when a panel is unreachable.

- **Notes to the clipboard.** A title-bar button on notes panels and `Ctrl+Shift+C`. The key
  binding is registered only on notes panels, because `Ctrl+Shift+C` is DevTools inspect inside
  WebView2 and a binding on the shared control would swallow it on every browser panel. The
  clipboard call is wrapped: any process can hold the clipboard open and Windows fails the call
  rather than waiting, and an unhandled throw would take an always-on-top window with it.
  `Controls/PanelStatusEventArgs.cs` is the channel panels use to ask the dock for a status
  message, since a status strip per panel would put the same sentence in six places.

- **Bookmarks and recent addresses.** Bookmarks are global (`AppSettings.Bookmarks`); recents
  belong to the layout (`OverlayLayout.RecentUrls`, schema version 3, cap 12) so they travel with
  export and import and a game workspace does not fill with what was read in a work one.
  `Services/UrlHistory.cs` holds the push, dedup, cap, and display-shortening as pure functions.
  The picker is an overlay inside the panel rather than a WPF `Popup`: a `Popup` is its own
  window and every DriftDeck window is topmost, which is exactly where popup ordering goes
  wrong. Panels pull the two lists each time the picker opens rather than binding to them, so a
  bookmark added in one panel needs no change notification to appear in the next. Recents are
  recorded on a successful `NavigationCompleted` — recording on `NavigationStarting` would fill
  the list with typos and dead hosts. `SettingsWindow` rebuilds `AppSettings` from scratch on
  Save, so it now carries `Bookmarks` across explicitly.

## In progress

Nothing. Three pull requests merged; the Tier 4 batch above is on
`feat/gather-notes-bookmarks` and has never been executed.

## Next up

### Blocking the first release

1. **Smoke-test a build.** Owner action; the assistant cannot run WPF — and cannot even compile
   locally, since the machine has SDK 8 against a `net10.0` target, so CI is the only compiler in
   the loop. Grab the portable ZIP from the last passing CI run, or `.\scripts\Build-Portable.ps1`.
   Watch, in order of how likely each is to be wrong:
   - the bookmarks/recents picker: it is an in-panel overlay, so check it is not clipped by a
     short panel and that Escape and clicking another panel both close it
   - `Ctrl+Shift+C` on a notes panel, and that it is still DevTools inspect on a browser panel
   - the gather button against a panel dragged off-screen, and with a dock parked at the bottom
   - the refreshed dock (146 px tall, two-row toolbar) at its 860 minimum width — five create buttons share the top row, and 860 is an estimate, on the
     smallest display in use, and that collapse/restore still lands on the 340 x 30 strip
   - hide and restore the overlay — `TrySuspendAsync` has a visibility precondition, and
     collapsing the control to satisfy it is the least certain call in the batch
   - `Ctrl+Shift+M` against `Ctrl+M`, to confirm WPF input-binding precedence
   - the Settings window with three sections added — it is `SizeToContent="Height"` under a
     `MaxHeight`, so it should scroll rather than clip
   - checklist, timer, and image panels at the restyled sizes (merged from `panel-types-and-tests`
     after the restyle, so never seen together); `Ctrl+T` new timer against `Ctrl+Shift+T` reopen
   - unplug a monitor with panels on it, and resume from sleep

2. **Tag v0.3.0.** Fill the date into `CHANGELOG.md`, then `git tag v0.3.0` and push it. This is
   the first execution of the `release` job and of `download-artifact@v8`, neither of which has
   ever run — the job is gated on `refs/tags/v*` and reports as skipping on every build so far.
   Expect to watch it.

### Features, in the order they are worth doing

3. **Tray panel list.** The tray menu is four fixed items. Listing open panels gives a way to
   reach one without the dock.

4. **Markdown notes panel type**, the one proposed panel type still missing.

5. **Keyboard accessibility.** Tab traversal across the dock and panels, and
   `AutomationProperties` on the controls that still lack them.

### Parked, with a reason

- **Tests** for `LayoutBundle`, `QuickLayout`, `Gather`, `UrlHistory` — the suite from
  `panel-types-and-tests` predates them.
- **Code signing**, once a certificate exists. Azure Trusted Signing is the cheapest route that
  works from GitHub Actions. Note this gates a *good* first release rather than any release:
  SmartScreen warns on every download until it exists.
- **Installer or `winget` package**, so an available update is not a manual ZIP swap.
- **Close with the host application.** Needs an explicit decision on the standing policy that
  DriftDeck never asks whether a game is running. That is a deliberate change of stance, not a
  feature to slip in.
