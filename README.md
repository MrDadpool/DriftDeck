# DriftDeck

DriftDeck is an open-source Windows overlay workspace for games and other fullscreen or borderless applications. It provides independent, always-on-top browser and notes panels without inspecting or modifying the application underneath.

DriftDeck does not read game memory or files, hook rendering, inject code, automate input, extract game state, or communicate with anti-cheat software.

## Features

- Independent browser and notes windows that can move anywhere on the Windows virtual desktop
- Multi-monitor placement, including monitors with negative desktop coordinates
- Interactive and native click-through modes
- Composition-hosted WebView2 content, so opacity and pass-through apply to the full browser panel
- Per-panel opacity, 50-150% content scaling, per-panel mute, and a panel lock
- Panel duplication, so a tuned panel does not have to be rebuilt by hand
- Optional idle dimming for panels you have not touched
- Overlay-wide panel transparency control
- Compact panel title bars with drag, resize, zoom, opacity, and close controls
- Named layouts with automatic save and last-layout restore
- Per-application layout rules that load the right layout when you switch to a game
- First-run tour covering pass-through, the global shortcuts, and the first panels
- Recovery notice and a local crash log when a session ends unexpectedly
- Optional startup check for a newer GitHub release
- Layout copy, confirmed deletion, and export or import of every layout as one file
- Automatic recovery when a monitor is disconnected, rescaled, or the machine resumes
- A plain warning when an application is in exclusive fullscreen, instead of an invisible overlay
- Configurable global hotkeys with reserved-shortcut validation and rollback
- Optional start with Windows, a start-hidden preference, and a single-instance launcher
- `Ctrl+Alt+1` to `Ctrl+Alt+9` load layouts without leaving the application underneath
- Browser panels pause while the overlay is hidden, so nothing renders behind a game
- Transparent workspace: only the dock and floating panels are rendered
- Collapsible dock that shrinks to the bottom-right of the current monitor instead of the taskbar

## Using DriftDeck

Start DriftDeck and use the compact dock to add browser or notes panels. Every panel is a separate desktop window.

### Panels

- Drag a panel using its title bar.
- Resize it with the lower-right grip.
- Use `-` and `+` in the title bar to change content scale.
- Use the title-bar slider to change that panel's opacity.
- Use `x` to close it.
- The copy button duplicates the panel; the padlock locks it in place so a stray drag cannot move or resize it.
- Browser panels accept HTTP and HTTPS addresses, can be muted individually, and can open the current address in the default browser.

### Dock

- `+ WEB` creates a browser panel.
- `+ NOTES` creates a notes panel.
- `SEE-THROUGH` changes all panel windows, including their content.
- The speaker button mutes or unmutes every browser panel at once.
- `_` collapses the dock to a 250 x 21 strip at the bottom-right of its current monitor.
- `□` restores a collapsed dock to its exact previous position and size.
- Closing the dock exits DriftDeck and closes all panels.

### Input modes

Interactive mode lets DriftDeck receive pointer and keyboard input. Pass-through mode makes the dock and every panel transparent to native Windows hit testing, including browser content, so input and cursor behavior come from the application underneath.

The default shortcuts are:

| Action | Default shortcut |
| --- | --- |
| Toggle interactive/pass-through | `Ctrl+Alt+O` |
| Hide/restore the complete overlay | `Ctrl+Alt+H` |

Panel and dock shortcuts, active while DriftDeck has focus:

| Action | Shortcut |
| --- | --- |
| Duplicate the focused panel | `Ctrl+D` |
| Lock or unlock the focused panel | `Ctrl+Shift+L` |
| Mute or unmute the focused browser panel | `Ctrl+Shift+M` |
| Mute or unmute every browser panel | `Ctrl+Shift+A` |

Quick-layout shortcuts are global like the two above, and are assigned under `SETTINGS`:

| Action | Shortcut |
| --- | --- |
| Load the layout assigned to a digit | `Ctrl+Alt+1` … `Ctrl+Alt+9` |

Shortcuts are configurable under `SETTINGS`. Saved settings from an existing installation may contain different shortcuts. DriftDeck rejects common Windows-reserved combinations such as `Alt+Space`.

### Layouts

A layout stores dock geometry, panel types, desktop coordinates, sizes, URLs, notes, opacity, and content scale. Use the editable layout selector with `LOAD`, `SAVE`, `COPY`, and `DEL`.

### Quick layouts

Per-application rules cover switching that should happen by itself. Quick layouts cover the other
half: deciding you want a different workspace right now. Assign up to nine layouts to
`Ctrl+Alt+1` through `Ctrl+Alt+9` under `SETTINGS`, and they load without clicking the dock —
which matters because clicking the dock means taking focus off a fullscreen application.

A digit another program already owns is reported in the status strip on the next launch rather
than failing quietly, so it can be reassigned. Loading a layout this way pauses automatic
switching until you move to a different application, exactly as the `LOAD` button does.

### Start with Windows

`SETTINGS` can register DriftDeck to start when you sign in. It is a per-user entry, appears in
Task Manager's Startup tab so it can also be disabled from there, and never needs elevation.
Moving or renaming the DriftDeck folder is handled: the entry is repointed at the new location on
the next launch instead of silently launching nothing.

### Panels that stay out of the way

Two optional behaviours under `SETTINGS`, both off or plainly explained rather than assumed:

- **Idle dimming** fades panels you have not touched for a while, so a reference page left open stops competing for attention. The panel you are working in and any panel the pointer is resting over never fade. Off by default; the delay and how far a panel fades are both configurable.
- **Locking** a panel refuses moves and resizes while leaving everything else — scale, opacity, roll-up, close — available. Locks are saved with the layout.
- **Pausing hidden panels** stops browser panels rendering, decoding video, and running page timers while the overlay is hidden, so nothing is spending graphics and processor time you hid the overlay to reclaim. On by default. A panel that is audibly playing and not muted is left running, and the whole behaviour can be turned off for a page that has to hold a live connection open.

### Displays that change

Layouts store desktop coordinates, so undocking a laptop, switching a second monitor off, a DPI change, or a driver reset can leave a panel parked where no display reaches. DriftDeck listens for those events and pulls the dock and every panel back into a real work area, then saves, so the next launch does not restore the unreachable position. The status strip says how many panels were moved.

### Exclusive fullscreen

Exclusive fullscreen is the one mode no ordinary desktop overlay can appear over. Rather than looking broken, DriftDeck says so in the status strip and the tray and suggests borderless mode. It asks Windows a single question about the desktop's own presentation state — the same question a notification asks before it appears — and never which application is responsible. Turn the notice off under `SETTINGS`.

### Exporting and importing layouts

`SETTINGS` can write every saved layout to one `.driftdeck` file and read one back. An imported layout whose name is already taken is added as `Name (imported)` rather than written over yours. Use it to back a workspace up, move it to another machine, or hand it to someone else.

### Per-application layouts

Turn on **Load a layout automatically when I switch to a matching application** under `SETTINGS` and add a rule per application. A rule matches an executable name, optionally narrowed by a substring of the window title, and names the layout to load. Rules that name a title win over rules that only name the executable, so one launcher can drive several layouts.

Use **Capture the next app I switch to** instead of typing an executable name: click it, click the application you want, and the name is filled in after four seconds.

A layout loads about a second after the application comes to the front, and only if it is still in front by then, so tabbing past a window does not load its layout. Loading a layout by hand pauses automatic switching until you move to a different application.

Matching uses only the foreground window's process name and title, which Windows publishes to every desktop application. Nothing is read out of the other process.

### Recovery and crash logs

DriftDeck writes the current layout within about 650 ms of every change, so an interrupted session loses nothing. If a run ends without quitting — most often because the game it was sitting over was killed with it — the next launch says so in the status strip and keeps a report in `%LOCALAPPDATA%\DriftDeck\logs`. `SETTINGS` has a button to open that folder. Logs are capped at 1 MB per file and the newest fourteen files are kept, so a repeated fault cannot fill the disk. Nothing is sent anywhere.

### Updates

DriftDeck can ask GitHub for the latest published release when it starts, and says so in the status strip and the tray if a newer one exists. The request is anonymous and carries nothing about you or the applications you run; turn it off under `SETTINGS`. Because DriftDeck ships as a portable folder, updating means downloading the new ZIP — nothing installs or replaces itself.

Layouts and settings are stored under `%LOCALAPPDATA%\DriftDeck`. Browser panels share one WebView2 profile under `%LOCALAPPDATA%\DriftDeck\webview2`, so a sign-in in one panel is available in the next and several panels cost far less memory than one browser process each. WebView2 manages those cookies and credentials itself; DriftDeck does not write them into layout JSON files.

## Compatibility and limitations

- Releases are not code-signed. Windows SmartScreen will warn the first time you run `DriftDeck.exe`; choose **More info**, then **Run anyway**. Download only from the [releases page](https://github.com/MrDadpool/DriftDeck/releases).
- Windows 10 version 2004 (build 19041) or later is required.
- Borderless-windowed mode is recommended for games.
- Exclusive fullscreen may prevent ordinary desktop overlays from appearing. DriftDeck detects this and says so rather than failing silently.
- Microsoft Edge WebView2 Runtime is required and is normally present on supported Windows systems.
- Website playback, sign-in, autoplay, and DRM support depend on the provider and WebView2 policies.
- DriftDeck does not guarantee compatibility with every game, graphics driver, HDR configuration, or third-party overlay.

## Build and run from source

Requirements:

- Windows 10 19041 or later
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime

```powershell
dotnet restore .\DriftDeck.slnx
dotnet build .\DriftDeck.slnx --configuration Debug
dotnet run --project .\src\DriftDeck\DriftDeck.csproj
```

## Portable Windows build

Create the same portable ZIP published by GitHub Actions:

```powershell
.\scripts\Build-Portable.ps1
```

The default output is `artifacts\DriftDeck-win-x64.zip`. It is a self-contained x64 build: users extract it and run `DriftDeck.exe` without installing the .NET runtime. Use `-FrameworkDependent` to create a smaller build that requires the .NET 10 desktop runtime.

## GitHub builds and releases

The workflow at `.github/workflows/windows-build.yml` builds pull requests and pushes to `main`, then uploads the portable ZIP as a workflow artifact.

Pushing a version tag creates or updates a GitHub Release:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

A tagged build stamps the tag into the executable version, so a released build can tell whether a newer release exists. Keep `<Version>` in `src/DriftDeck/DriftDeck.csproj` in step with the tag you intend to publish.

The workflow attaches `DriftDeck-win-x64.zip` to that release. This directory has not been initialized or pushed by the development assistant; repository creation and the initial push remain owner actions.

## Project structure

```text
src/DriftDeck/                  WPF application
  Controls/PanelHost.*         Browser and notes panel UI
  Models/                      Persisted settings and layout models
  Services/                    Hotkeys, persistence, and Win32 window behavior
.github/workflows/             GitHub build and release automation
scripts/Build-Portable.ps1     Reproducible portable publisher
SC_Overlay.md                  Original product brief
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for development expectations, [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the window and safety design, and [docs/ROADMAP.md](docs/ROADMAP.md) for planned work.

## License

DriftDeck is released under the [MIT License](LICENSE).
