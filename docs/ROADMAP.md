# DriftDeck roadmap

This roadmap records intended work, not guarantees or release dates.

## Next priorities

- [ ] Add bottom-edge collapse and restore controls to every browser and notes window.
  - Each panel should remember its expanded position and size.
  - Collapsed panels should remain as compact, identifiable strips along the current monitor's work-area bottom.
  - Collapsed geometry must not overwrite the saved expanded layout.
  - Global hide/restore and pass-through must continue to affect collapsed panels.

- [ ] Add an optional "close with host application" lifecycle setting.
  - Keep the feature game-neutral: the user explicitly selects an executable to observe.
  - DriftDeck should close after the selected application exits.
  - Observation must be limited to ordinary OS process-presence information.
  - Do not open process handles, read memory or files, inspect rendering, extract game state, automate input, or interact with anti-cheat software.
  - This requires an explicit review of the current strict policy that DriftDeck never queries whether a game is running.

- [ ] Harden notes persistence between sessions.
  - Notes already save in named layout JSON and restore with that layout.
  - Add automated save/restore tests.
  - Add crash-safe recovery so recent edits survive an abnormal shutdown.
  - Verify switching, copying, deleting, and renaming layouts cannot silently lose notes.

## Later work

- [ ] First-launch onboarding for hotkeys, compatibility, and the safety boundary.
- [ ] Display-aware recovery when a saved monitor is disconnected or its DPI changes.
- [ ] Keyboard accessibility and visible focus improvements.
- [ ] Panel duplication.
- [ ] Automated tests for layout persistence, panel geometry, and hotkey parsing.
- [ ] Optional code signing for published Windows builds.

