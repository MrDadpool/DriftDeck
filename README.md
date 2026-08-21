# DriftDeck

DriftDeck is an open-source Windows overlay workspace for games and other fullscreen or borderless applications. It provides independent, always-on-top browser and notes panels without inspecting or modifying the application underneath.

DriftDeck does not read game memory or files, hook rendering, inject code, automate input, extract game state, or communicate with anti-cheat software.

## Features

- Independent browser and notes windows that can move anywhere on the Windows virtual desktop
- Multi-monitor placement, including monitors with negative desktop coordinates
- Interactive and native click-through modes
- Composition-hosted WebView2 content, so opacity and pass-through apply to the full browser panel
- Per-panel opacity and 50-150% content scaling
- Overlay-wide panel transparency control
- Compact panel title bars with drag, resize, zoom, opacity, and close controls
- Named layouts with automatic save and last-layout restore
- Per-application layout rules that load the right layout when you switch to a game
- First-run tour covering pass-through, the global shortcuts, and the first panels
- Recovery notice and a local crash log when a session ends unexpectedly
- Optional startup check for a newer GitHub release
- Layout copy and confirmed deletion
- Configurable global hotkeys with reserved-shortcut validation and rollback
- Start-hidden preference and a single-instance launcher
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
- Browser panels accept HTTP and HTTPS addresses and can open the current address in the default browser.

### Dock

- `+ WEB` creates a browser panel.
- `+ NOTES` creates a notes panel.
- `SEE-THROUGH` changes all panel windows, including their content.
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

Shortcuts are configurable under `SETTINGS`. Saved settings from an existing installation may contain different shortcuts. DriftDeck rejects common Windows-reserved combinations such as `Alt+Space`.

### Layouts

A layout stores dock geometry, panel types, desktop coordinates, sizes, URLs, notes, opacity, and content scale. Use the editable layout selector with `LOAD`, `SAVE`, `COPY`, and `DEL`.

### Per-application layouts

Turn on **Load a layout automatically when I switch to a matching application** under `SETTINGS` and add a rule per application. A rule matches an executable name, optionally narrowed by a substring of the window title, and names the layout to load. Rules that name a title win over rules that only name the executable, so one launcher can drive several layouts.

Use **Capture the next app I switch to** instead of typing an executable name: click it, click the application you want, and the name is filled in after four seconds.

A layout loads about a second after the application comes to the front, and only if it is still in front by then, so tabbing past a window does not load its layout. Loading a layout by hand pauses automatic switching until you move to a different application.

Matching uses only the foreground window's process name and title, which Windows publishes to every desktop application. Nothing is read out of the other process.

### Recovery and crash logs

DriftDeck writes the current layout within about 650 ms of every change, so an interrupted session loses nothing. If a run ends without quitting — most often because the game it was sitting over was killed with it — the next launch says so in the status strip and keeps a report in `%LOCALAPPDATA%\DriftDeck\logs`. `SETTINGS` has a button to open that folder. Nothing is sent anywhere.

### Updates

DriftDeck can ask GitHub for the latest published release when it starts, and says so in the status strip and the tray if a newer one exists. The request is anonymous and carries nothing about you or the applications you run; turn it off under `SETTINGS`. Because DriftDeck ships as a portable folder, updating means downloading the new ZIP — nothing installs or replaces itself.

Layouts and settings are stored under `%LOCALAPPDATA%\DriftDeck`. WebView2 manages browser cookies and credentials separately; DriftDeck does not write them into layout JSON files.

## Compatibility and limitations

- Releases are not code-signed. Windows SmartScreen will warn the first time you run `DriftDeck.exe`; choose **More info**, then **Run anyway**. Download only from the [releases page](https://github.com/MrDadpool/DriftDeck/releases).
- Windows 10 version 2004 (build 19041) or later is required.
- Borderless-windowed mode is recommended for games.
- Exclusive fullscreen may prevent ordinary desktop overlays from appearing.
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
