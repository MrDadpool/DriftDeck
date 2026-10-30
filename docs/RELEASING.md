# Releasing DriftDeck

## Local release check

From the repository root:

```powershell
dotnet restore .\DriftDeck.slnx
dotnet build .\DriftDeck.slnx --configuration Debug --no-restore
dotnet build .\DriftDeck.slnx --configuration Release --no-restore
dotnet test .\DriftDeck.slnx --configuration Release --no-build
.\scripts\Build-Installer.ps1
```

Or download the `DriftDeck-installer` artifact from the latest passing `Windows build` run.

Run the `Setup.exe` from `artifacts\releases`. It installs per user, without an admin prompt, to
`%LOCALAPPDATA%\DriftDeck.App`. Verify:

- Setup adds shortcuts and launches DriftDeck; the first-run tour appears.
- The dock, a browser panel, and a notes panel open.
- Interactive/pass-through and hide/restore shortcuts work.
- Browser content and chrome change opacity together.
- The native cursor belongs to the application underneath in pass-through mode.
- Panels move and persist independently across available monitors.
- Content scale, notes, URLs, opacity, and named layouts restore.
- Dock collapse and restore preserve the expanded geometry.
- A second launch restores the existing instance rather than creating a duplicate.

## GitHub Release

Commit the intended release state, push it to `main`, then create and push a semantic version tag:

```powershell
git tag v0.3.0
git push origin v0.3.0
```

The Windows workflow builds and tests the source, builds the Velopack installer with a delta against the previous release, creates the GitHub Release, and uploads `Setup.exe` with the update packages. Release notes come from the tag's section of `CHANGELOG.md`.

Confirm the release notes and asset before public announcement. Releases are unsigned until a code-signing workflow is added, so Windows may show a reputation warning.

