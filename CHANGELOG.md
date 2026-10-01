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
  panels cost far less memory, and browser state never lives in the install folder, which every
  update replaces.
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
- **Gather every panel onto the current monitor** from the dock. Display recovery already handled
  a monitor disappearing; this handles a panel dragged somewhere with no title bar left on screen
  to grab. Panels are cascaded so each title bar stays clickable, sizes are untouched, and locked
  panels move too — the lock refuses accidental drags, not deliberate commands.
- **Copy a notes panel to the clipboard** from its title bar or with `Ctrl+Shift+C`. Notes
  previously lived only inside the layout file, which made a notes panel somewhere text went in
  and never came out.
- **Bookmarks and recent pages** behind the chevron beside a browser panel's address box.
  Bookmarks are shared by every layout; recent addresses belong to the layout they were opened
  in, newest first and capped at twelve. Typing a URL into an 18-pixel toolbar during a game was
  the worst interaction left in the product.

### Installer and updates

- **One-click installer.** `DriftDeck.App-win-Setup.exe` installs for the current user with no
  administrator prompt, adds Start menu and desktop shortcuts, and installs the WebView2 runtime
  if it is missing. There is no portable ZIP.
- **Updates you approve.** DriftDeck still only tells you a release exists. **Update and
  restart** in Settings downloads it, closes DriftDeck the normal way so the layout is saved,
  installs it, and starts the new version. It never downloads or restarts on its own.
- Uninstalling leaves layouts, settings, and the browser profile in `%LOCALAPPDATA%\DriftDeck`,
  and removes the start-with-Windows entry so it does not point at a deleted folder.

### Known limitations

- Releases are not code-signed, so Windows SmartScreen warns the first time you run the
  installer. Choose **More info**, then **Run anyway**.
- Exclusive fullscreen still prevents the overlay from appearing; DriftDeck can only tell you that
  is what is happening.

## v0.2.0 and earlier

Not published. Source-only development of the overlay shell, panels, layouts, per-application
layout rules, the first-run tour, crash recovery, and the update check.
