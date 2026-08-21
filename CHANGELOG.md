# Changelog

This file records what changed between published releases. Dates are the release date, not the
date the work was done.

## v0.3.0 — unreleased

First published release. Everything below is new to anyone who has not built from source.

### Overlay reliability

- **Exclusive-fullscreen notice.** Exclusive fullscreen is the one desktop state where an
  ordinary always-on-top window is not composited over the foreground application. DriftDeck now
  says so in the status strip and the tray and suggests borderless mode, instead of appearing
  broken. It asks Windows a single question about the desktop's presentation state — the same one
  a notification asks before appearing — and never which application is responsible.
- **Display recovery.** Undocking a laptop, switching a monitor off, a DPI change, or a driver
  reset could leave panels parked at coordinates no display covered any more: present, topmost,
  and unreachable. The dock and every panel are now pulled back into a real work area whenever the
  display set changes, and the layout is saved so the next launch does not restore the unreachable
  position.
- **Hidden panels stop working.** Hiding the overlay previously hid the windows and nothing else;
  every browser panel kept rendering, decoding video, and running page timers. Browser panels are
  now paused while the overlay is hidden. A panel that is audibly playing and not muted is left
  running, and the whole behaviour can be switched off for a page that must hold a live connection.
- **Shared browser profile.** Browser panels share one WebView2 profile under
  `%LOCALAPPDATA%\DriftDeck\webview2`. A sign-in in one panel now carries to the next, several
  panels cost far less memory, and a portable folder no longer grows browser state beside the
  executable at runtime.
- **Crash logs are bounded.** Logs are capped at 1 MB per file and pruned to the newest fourteen,
  so a repeated fault cannot fill the disk.

### Reaching layouts and panels

- **Quick layouts.** Assign up to nine layouts to `Ctrl+Alt+1` through `Ctrl+Alt+9` and load them
  without clicking the dock — which matters, because clicking the dock takes focus off a
  fullscreen application. A digit another program already owns is reported rather than failing
  quietly.
- **Start with Windows.** Optional, per-user, and listed in Task Manager's Startup tab so it can
  be turned off from there too. Moving the DriftDeck folder is handled: the entry is repointed on
  the next launch.
- **Export and import layouts.** Every saved layout as one `.driftdeck` file, for backup, moving
  machines, or sharing a setup. An imported layout whose name is taken is added as
  `Name (imported)` rather than written over yours.

### Panels

- **Duplicate a panel** with the title-bar button or `Ctrl+D`, instead of rebuilding a tuned panel
  by hand.
- **Lock a panel** with the padlock or `Ctrl+Shift+L`. Moves and resizes are refused; scale,
  opacity, roll-up, and close all still work, because the accident being prevented is a stray
  drag during a game.
- **Mute browser audio** per panel (`Ctrl+Shift+M`) or across every browser panel at once
  (`Ctrl+Shift+A`). Muting silences without pausing.
- **Idle dimming**, off by default. Panels you have not touched fade so they stop competing for
  attention. The panel you are working in and any panel the pointer is resting over never fade.

### Known limitations

- Releases are not code-signed, so Windows SmartScreen warns the first time you run
  `DriftDeck.exe`. Choose **More info**, then **Run anyway**.
- Updating means downloading the new ZIP. DriftDeck is a portable folder and does not replace
  itself.
- Exclusive fullscreen still prevents the overlay from appearing; DriftDeck can only tell you that
  is what is happening.

## v0.2.0 and earlier

Not published. Source-only development of the overlay shell, panels, layouts, per-application
layout rules, the first-run tour, crash recovery, and the update check.
