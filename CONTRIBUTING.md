# Contributing to DriftDeck

Thanks for helping improve DriftDeck.

## Development setup

- Windows 10 19041 or later
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime

Build the project with:

```powershell
dotnet restore .\DriftDeck.slnx
dotnet build .\DriftDeck.slnx --configuration Debug
```

Run it with:

```powershell
dotnet run --project .\src\DriftDeck\DriftDeck.csproj
```

Create a portable test build with:

```powershell
.\scripts\Build-Portable.ps1
```

Architecture, roadmap, and release details are documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/ROADMAP.md](docs/ROADMAP.md), and [docs/RELEASING.md](docs/RELEASING.md).

## Product boundary

Contributions must preserve DriftDeck's standalone desktop-only design. Do not add game process inspection, memory or log extraction, rendering hooks, injection, automated game input, anti-cheat interaction, or game-state detection.

Keep browser credentials, cookies, and authentication data out of layout JSON files and source control.

## Pull requests

- Keep changes focused and explain the user-visible behavior.
- Build both Debug and Release configurations.
- Include validation steps for window behavior, persistence, and global input changes.
- Update user or architecture documentation when behavior changes.
- Use original or appropriately licensed visual assets only.
