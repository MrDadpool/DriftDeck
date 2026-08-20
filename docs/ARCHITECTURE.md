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

Layout writes use a temporary file followed by replacement. Layouts contain panel configuration and built-in notes, but no browser passwords, cookies, or authentication tokens. Browser profile data is managed by WebView2.

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

