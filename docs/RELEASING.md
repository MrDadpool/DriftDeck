# Releasing DriftDeck

## Local release check

From the repository root:

```powershell
dotnet restore .\DriftDeck.slnx
dotnet build .\DriftDeck.slnx --configuration Debug --no-restore
dotnet build .\DriftDeck.slnx --configuration Release --no-restore
.\scripts\Build-Portable.ps1
```

Extract `artifacts\DriftDeck-win-x64.zip` into a temporary directory and launch `DriftDeck.exe`. Verify:

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
git tag v0.1.0
git push origin v0.1.0
```

The Windows workflow builds the source, creates the self-contained archive, creates the GitHub Release when needed, and uploads `DriftDeck-win-x64.zip`.

Confirm the release notes and asset before public announcement. Releases are unsigned until a code-signing workflow is added, so Windows may show a reputation warning.

