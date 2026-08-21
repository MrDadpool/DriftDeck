# DriftDeck architecture

## Process boundary

DriftDeck runs as one ordinary Windows desktop process. It has no technical connection to a game or other application underneath it.

```text
Global hotkey service
        |
Compact dock window -------- Settings and named layout stores
        |
        +---- Browser panel window ---- WebView2CompositionControl
        +---- Browser panel window ---- WebView2CompositionControl
        +---- Notes panel window  ----- WPF text surface

Game or desktop application: separate process and data boundary
```

The dock and panels are separate top-level, borderless, always-on-top windows. Panel coordinates are absolute positions in the Windows virtual desktop rather than offsets inside a parent canvas. This allows panels to cross monitor boundaries and use negative coordinates.

## Rendering

The root area is transparent. Only the compact dock and panel surfaces render.

Browser panels use `WebView2CompositionControl` rather than the standard HWND-hosted WPF WebView2 control. Composition hosting keeps browser pixels in the WPF composition path so top-level window opacity applies to the webpage and panel chrome together.

## Input modes

Interactive mode uses normal WPF and WebView2 input.

Pass-through mode applies `WS_EX_TRANSPARENT` and `WS_EX_NOACTIVATE` to every DriftDeck top-level window. The Win32 window hook also returns `HTTRANSPARENT` for `WM_NCHITTEST`. Together, these make native hit testing, input, and cursor selection resolve to the application beneath DriftDeck.

Global hotkeys are registered with `RegisterHotKey` and `MOD_NOREPEAT`. If Windows rejects updated shortcuts, DriftDeck restores the previously registered pair.

## Persistence

Settings are written to:

```text
%LOCALAPPDATA%\DriftDeck\settings.json
```

Named layouts are written to:

```text
%LOCALAPPDATA%\DriftDeck\layouts\<name>.json
```

Layout writes use a temporary file followed by replacement. Layouts contain panel configuration and built-in notes, but no browser passwords, cookies, or authentication tokens.

Browser profile data is managed by WebView2 in one environment shared by every panel, rooted at:

```text
%LOCALAPPDATA%\DriftDeck\webview2
```

Pinning it matters twice over: a portable folder must not accumulate runtime state beside the executable, and one environment lets panels share sign-ins and reuse browser processes instead of standing up a separate group per panel.

Every saved layout can be exported to, and imported from, a single `.driftdeck` bundle. Import never overwrites: a colliding name is stored as `Name (imported)`.

## Display changes

A layout is a list of virtual-desktop coordinates, so any change to the display set can strand a panel outside every monitor while it remains present and topmost. `Services/DisplayWatcher.cs` subscribes to `SystemEvents.DisplaySettingsChanged`, resume from `PowerModeChanged`, and unlock or console-connect from `SessionSwitch`, debounced so a burst of per-monitor notifications is handled once. `PanelWindow` additionally handles `DpiChanged`. Each settled change re-clamps the dock and every panel into a live work area and then saves, because leaving the unreachable coordinates on disk would reproduce the problem on the next launch.

`SystemEvents` keeps a process-wide static subscription list and raises on its own thread, so handlers marshal to the dispatcher and are unsubscribed on dispose.

## Presentation state

`Services/FullscreenProbe.cs` calls `SHQueryUserNotificationState` and reports the single transition into and out of exclusive-fullscreen Direct3D presentation — the one desktop state in which an ordinary always-on-top window is not composited over the foreground application. This is the same question the shell answers before deciding whether a notification may appear. It identifies no process, opens no handle, and installs no hook; it exists so that an overlay which cannot be shown says so rather than appearing broken.

## Lifecycle

DriftDeck can register itself under the per-user `Software\Microsoft\Windows\CurrentVersion\Run` key. A Startup-folder shortcut would need COM shell interop to create; a machine-wide key would need elevation for what is a portable folder owned by one user. The registry value is the single source of truth — it is also user-visible and user-disableable in Task Manager's Startup tab, so a duplicate flag in `settings.json` could disagree with reality. A stale value left behind by moving the folder is repointed on the next launch.

While the overlay is hidden, browser panels are suspended through `CoreWebView2.TrySuspendAsync`. WebView2 declines to suspend visible content, so the control is collapsed first; resume happens before the windows are shown again. Panels audibly playing unmuted audio are skipped.

Crash logs are capped at 1 MB per file, roll to `crash-<date>.<n>.log` rather than truncating, and are pruned to the newest fourteen files.

## Global shortcuts

Two mode shortcuts — pass-through and hide — are required for the overlay to be usable at all, so a failure to register either is fatal to the service and surfaces as a validation error with a rollback to the previous configuration.

Quick-layout shortcuts (`Ctrl+Alt+1` … `Ctrl+Alt+9`) are a convenience over the same `RegisterHotKey` mechanism and are treated differently: refusals are collected and reported, never thrown. A digit already owned by another application must not be able to cost the user their pass-through shortcut. Slots are stored explicitly rather than derived from layout ordering, so saving a new layout cannot silently remap existing shortcuts.

## Single-instance behavior

A named local mutex prevents duplicate DriftDeck processes. A second launch posts a registered Windows message to the existing dock, which restores and activates the current instance.

## Safety boundary

The following are architectural non-goals:

- Game process handles, memory reads, or injection
- Game file, configuration, registry, or log inspection
- Graphics API, window-buffer, shader, or rendering hooks
- OCR or game-state detection
- Gameplay automation, macros, or synthesized game input
- Network packet inspection
- Anti-cheat probing, bypass, or interference

Features must remain useful as ordinary desktop capabilities above any application.

