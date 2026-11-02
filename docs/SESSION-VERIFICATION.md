# Session and dock verification

Run the Windows build with WebView2 installed. These checks require the real Windows runtime; a successful cross-build or persistence unit test does not prove them.

1. Open a notes panel, type a distinctive sentence, move and resize it, change its opacity, then immediately quit. Restart and verify the sentence and geometry survived. Repeat while an autosave is in progress.
2. Close every panel, quit, and restart. The layout must stay empty.
3. Create two browser panels with different mute states. Save a named layout, switch away, and switch back. Each panel must retain its own mute choice, zoom, opacity, title, lock, and rolled-up state.
4. Collapse the dock, quit, and restart. It must remain collapsed. Restore it and verify the previous expanded size and location. Switch between layouts with different collapsed states.
5. Sign into a site that uses persistent cookies, close and restart DriftDeck, and verify the session. Repeat with a provider that opens an authentication window: it must be able to return its result and close without navigating the original panel away. Do not include credentials or tokens in screenshots or logs.
6. Open a login window, then use the global hide and pass-through shortcuts. The login window must follow its parent. Close the parent or switch layouts and verify the login window closes.
7. Start typing a different URL without submitting it, quit, and restart. The last visited page must be restored.
8. At 100% and 200% display scaling, check the dock at its minimum and restored widths. Add every panel type, select a layout, use More, adjust fading, reopen a panel, and confirm a deletion. Open a menu before using hide or pass-through; the menu must disappear too. Check the unchanged notes editor for a layout or focus regression.
9. In a disposable test profile, save settings/layouts twice and damage a primary JSON file. Restart and verify recovery from `.bak` with a visible notice. The damaged original must remain available after the next save.
10. In a disposable test profile, make the storage directory unwritable. Edit a note and attempt to switch layouts or quit. The app must report unsaved changes and preserve the live note. Restore write access, retry, and verify the final edit survives restart.

The persistent browser directory remains `%LOCALAPPDATA%\DriftDeck\webview2`. Password prompts and cookies remain managed by WebView2. A site's session expiration or refusal of embedded browsers cannot be overridden by DriftDeck.
