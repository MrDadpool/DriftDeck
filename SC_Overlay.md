# Star Citizen Multitasking Overlay

## Project brief

### Product vision

Create a standalone Windows desktop companion that places small, optional web and media panels above Star Citizen. It should make downtime, especially quantum travel, more useful without needing to understand, modify, or control the game.

The product behaves like a polished floating desktop workspace: a player can watch a video, browse a site, control music, check a checklist, or use a web-based tool, then instantly return all input to the game. It is not an in-game mod, HUD replacement, automation tool, or source of game data.

### Design principles

- Be useful in quiet moments and unobtrusive everywhere else.
- Treat Star Citizen as an unrelated application. Never inspect or interact with its process.
- Make switching between passive and interactive overlay states immediate and obvious.
- Start with ordinary web content and native Windows capabilities before building service-specific integrations.
- Use a distinct visual system that evokes a practical spacecraft utility console without copying RSI, CIG, Star Citizen, or third-party brand assets.

## Primary use cases

### Quantum travel companion

During a long quantum jump, a player opens a compact panel to:

- Watch YouTube or another browser video in a corner.
- Control Spotify or Apple Music playback.
- Read a web page, wiki, Spectrum, Reddit, Discord web, or planning tool.
- Use a timer, notes, checklist, calculator, route planner, or other web widget.
- Switch to a saved "Quantum" workspace, then return to a minimal "Flight" workspace.

The overlay does not detect a quantum jump. The player decides when to use it.

### Persistent media controls

A small media panel can offer play, pause, next, previous, volume, and current-track information when the selected provider supports it. The first version should prefer browser tabs and Windows media-session controls over service-specific API work.

### General-purpose web workspace

Each panel is an isolated browser surface that can load an allowed URL, a local built-in utility, or a supported media view. Users can resize, move, snap, hide, duplicate, and save panels as layouts.

## Core experience

### Overlay modes

| Mode | What happens | Intended use |
| --- | --- | --- |
| Click-through | Overlay remains visible but does not receive pointer input. Keyboard and mouse continue to Star Citizen. | Passive video, media display, a timer, reference information. |
| Interactive | Overlay accepts pointer and keyboard input. The active panel is visibly framed. | Browsing, starting a video, changing music, arranging panels. |
| Hidden | Overlay is not visible and receives no input. | Full-screen gameplay or a quick reset. |

A global hotkey cycles or toggles these states. Default recommendation: `Alt+Space` toggles interactive mode, and `Alt+Shift+Space` hides or restores the overlay. Make hotkeys configurable and show a brief on-screen state indicator. Do not register combinations already reserved by Windows.

### Panels and layouts

- Panels have a title bar, drag handle, close button, opacity control, and a click-through indicator.
- Resize from edges and corners; enforce a sensible minimum size.
- Snap panels to screen edges, corners, and a simple half or quarter grid.
- Save named layouts, for example `Quantum`, `Mining`, `Trading`, and `Minimal`.
- A layout saves panel URLs or panel types, bounds, visibility, opacity, and the selected display. Do not persist site passwords, cookies, or authentication tokens directly.
- Restore the most recent layout on launch, with a safe option to start hidden.

## Visual direction

Aim for an original, restrained "flight utility" feel:

- Dark translucent surfaces, subtle noise or gradient, generous contrast, and one configurable accent color.
- Compact, readable typography and clear mode states.
- Thin borders, soft shadows, and quiet motion only for state changes.
- Generic geometric icons or an independently licensed icon set.

Avoid Star Citizen logos, ship imagery, UI screenshots, fonts, sound effects, proprietary terminology in the product branding, and copied interface layouts. "Space utility" is a direction, not a license to imitate protected assets.

## Safety and compliance guardrails

This is a strict product boundary, not a feature toggle.

The app must never:

- Inject code, DLLs, overlays, hooks, or shaders into Star Citizen or any game process.
- Read game memory, RAM regions, logs intended for game state extraction, packets, process handles, or window-rendering buffers.
- Modify game files, configuration files, registry values owned by the game, or anti-cheat settings.
- Automate gameplay, synthesize game inputs, macro actions, bot behavior, or perform actions based on game state.
- Bypass, disable, evade, probe, or interfere with Easy Anti-Cheat or any other anti-cheat software.
- Claim official approval, compatibility, or affiliation with Cloud Imperium Games or RSI.

The only relationship to the game is ordinary desktop composition: a separate transparent, always-on-top application window placed above the game. Review current CIG/RSI terms and support guidance before release, and make user-facing safety boundaries explicit.

## MVP scope

Build the smallest version that proves the main interaction:

1. A standalone Windows app with a transparent, always-on-top borderless window.
2. One or more WebView2 panels that load user-supplied URLs, plus a simple built-in notes or timer panel.
3. A global hotkey to toggle click-through and interactive mode, plus hide or show.
4. Drag, resize, close, and basic edge snapping.
5. Save and restore named layouts locally.
6. A visible settings screen for hotkeys, opacity, launch behavior, and the safety policy.
7. A small, original visual theme.

### Non-goals for MVP

- No game-state detection, telemetry, OCR, process inspection, or game integration.
- No game input macros or commands.
- No custom player account system, cloud sync, marketplace, plugin platform, or team sharing.
- No native Spotify or Apple Music API integration.
- No DRM circumvention, download feature, ad blocking, or playback manipulation.
- No attempt to support exclusive-fullscreen games as a guarantee.

## Suggested Windows technical stack

Use the native platform first:

- **Language and UI:** C# with WinUI 3 and Windows App SDK.
- **Embedded web:** Microsoft WebView2, which uses the installed Edge runtime.
- **Window behavior:** Win32 interop only where WinUI does not expose required functionality, such as extended styles for topmost and click-through behavior.
- **Layout persistence:** a small local JSON file in the app's local application-data folder.
- **Media controls:** Windows `GlobalSystemMediaTransportControlsSessionManager` where available, with browser content as the fallback.
- **Installer and updates:** MSIX packaging initially, unless global-hotkey or enterprise distribution requirements prove a packaged desktop app is unsuitable.

Why this stack: it minimizes dependencies, ships with Microsoft-supported web rendering, and keeps the overlay separate from the game process.

### Architecture

```text
Global hotkey service
        |
Overlay shell window (standalone, transparent, topmost)
        |
Panel manager ---- Layout store (local JSON)
        |
WebView2 panel hosts ---- User-selected websites / built-in local tools
        |
Windows media-session adapter (optional, capability-based)

Star Citizen: separate process, no API, no handle, no data path
```

Keep the boundary one-way and conceptual rather than technical: the overlay does not query whether Star Citizen is running. It can be used above any application or on the desktop.

## UX flows

### First launch

1. Explain that the app is a standalone desktop window and does not interact with games.
2. Let the player select a default layout, such as a single browser panel or media plus notes.
3. Ask them to choose hotkeys and whether to start hidden.
4. Open a panel gallery with YouTube, a blank URL panel, notes, and timer.
5. Show a short reminder: switch to interactive mode before clicking panels.

### Watch a video during travel

1. Press the restore hotkey.
2. Select the `Quantum` layout.
3. Enter interactive mode and choose a video or browser tab.
4. Press the interaction hotkey to return to click-through mode.
5. Continue playing while the panel remains passive above the game.
6. Hide the overlay when finished.

### Configure a web widget

1. Enter interactive mode.
2. Add a browser panel.
3. Enter a URL and complete any site sign-in inside that panel.
4. Resize or snap it, then save the layout.
5. Return to click-through mode.

## Platform and content caveats

### Borderless and full-screen behavior

Topmost desktop windows work most reliably when a game runs in borderless-windowed mode. True exclusive fullscreen may prevent the overlay from appearing above the game, depending on the game, graphics driver, HDR configuration, and Windows settings. Document this as a compatibility limitation, not something to solve by injecting into the game or hooking graphics APIs.

### Focus and input

Click-through requires careful window-style switching. Interactive mode will necessarily move focus to the overlay, and returning to click-through may require the user to click the game or use a hotkey. Test behavior with multi-monitor systems, alt-tab, Windows notifications, and game launcher overlays.

### Authentication, DRM, and playback

- Many sites require user sign-in, MFA, cookies, or a supported browser user agent. Let the site handle this inside WebView2.
- YouTube and other web video services can impose autoplay, playback, account, and embedded-player restrictions.
- Apple Music and some protected video services may not play in an embedded view due to DRM or browser-policy requirements.
- Spotify support may vary between the web player, native app, and media-session controls.
- Never attempt to bypass DRM, spoof entitlements, extract streams, or automate sign-in.

Treat a provider failure as normal: offer "Open in default browser" and plain media-session controls when Windows exposes them.

## Phased roadmap

### Phase 0: feasibility spike

- Create a blank transparent WinUI 3 window.
- Prove topmost, click-through, interactive toggle, and global hotkey behavior.
- Test above a normal app and a borderless game window without connecting to the game process.
- Record limitations for exclusive fullscreen, HDR, multi-monitor, and focus restoration.

### Phase 1: MVP

- Add WebView2 panels, a URL entry flow, built-in notes or timer, panel movement and resizing.
- Add simple snapping, local layout storage, and hidden or interactive or click-through states.
- Add clear safety copy and an original visual treatment.

### Phase 2: polish

- Add a layout picker, keyboard shortcuts, display targeting, opacity controls, and crash recovery.
- Add best-effort Windows media-session controls.
- Improve accessibility: keyboard navigation, visible focus, high contrast, readable text, and configurable hotkeys.

### Phase 3: validate demand

- Run a small opt-in test with ordinary desktop and borderless-window use.
- Collect feedback on panel size, focus friction, layouts, and media reliability.
- Only then consider opt-in cloud sync or tightly scoped provider integrations.

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Overlay does not appear above exclusive fullscreen | Recommend borderless-window mode; do not add invasive rendering hooks. |
| Input leaks to the wrong window | Make the current mode unmistakable, test extensively, and provide a hide hotkey. |
| Embedded media fails due to DRM or site policy | Use WebView2 normally, document variability, and offer open-in-browser fallback. |
| Hotkey conflicts | Default to configurable combinations and validate registration failures. |
| Website content harms privacy or performance | Do not proxy user traffic; provide a clear panel-origin indicator and allow users to close panels quickly. |
| Policy or anti-cheat concerns | Preserve strict separation from game processes and review official terms before shipping. |
| Users expect game awareness | State plainly that the app is manual and never detects gameplay. |

## Open questions

1. Should the app be positioned as a Star Citizen companion only, or as a universal gaming and desktop overlay with a Star Citizen-oriented layout pack?
2. Is the expected user willing to run Star Citizen in borderless-windowed mode?
3. Which matters more for the first release: reliable browser panels, media controls, or saved layouts?
4. Should browser panels be unrestricted URLs or limited to a user-managed allowlist?
5. Is MSIX distribution compatible with the desired global-hotkey and startup experience on supported Windows versions?
6. Does the product need per-monitor layouts and DPI-aware migration on display changes in MVP?

## First-development checklist

- [ ] Create a C# WinUI 3 desktop project and confirm it launches from a clean Windows machine.
- [ ] Add an original app name, icon, and safety statement. Do not use protected game branding or assets.
- [ ] Implement the borderless, transparent, topmost shell window.
- [ ] Add a configurable global hotkey and a clear visible state label.
- [ ] Implement click-through and interactive switching through standard Windows window styles.
- [ ] Embed a single WebView2 panel with a user-entered URL.
- [ ] Add one local timer or notes panel to prove non-web panel hosting.
- [ ] Implement drag, resize, close, and edge snapping.
- [ ] Save and restore one local layout as JSON, excluding credentials and cookies from the layout file.
- [ ] Test against standard windows, borderless-window games, alt-tab, multiple displays, scaling, and focus changes.
- [ ] Validate that the app never opens, reads, writes, injects into, or otherwise touches a game process.
- [ ] Review current CIG/RSI rules and Easy Anti-Cheat guidance before any public release.

## Definition of a successful MVP

A user can bring up a small YouTube or browser panel above a borderless game window, interact with it using one hotkey, return the mouse and keyboard to the game with another press, resize and save the panel arrangement, and hide it instantly. The app remains a normal desktop window throughout, with no access to game code, files, memory, inputs, rendering, or anti-cheat systems.
