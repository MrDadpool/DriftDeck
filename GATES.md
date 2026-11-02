# Gates: Reliable everyday use

OWNS: src/DriftDeck/**, tests/**, scripts/**, docs/**, README.md, CHANGELOG.md, DriftDeck.slnx, GATES.md

Scope: Preserve settings and layouts through saves and restarts, correct browser sign-in handling, and reduce routine layout-management friction.

- [x] G1: Persistence regressions pass, including empty layouts, recovery and overlapping saves.
  CHECK: DOTNET_CLI_HOME=/tmp/driftdeck-build-home NUGET_PACKAGES=/tmp/driftdeck-nuget /tmp/driftdeck-dotnet/dotnet test tests/DriftDeck.Persistence.Tests/DriftDeck.Persistence.Tests.csproj --no-restore
  EXPECT: Passed!
  EVIDENCE: shell=/bin/zsh; cwd=/Users/davidmartinez/GitHub/3. Computer Apps/2. In Progress/DriftDeck; PATH_SHA256=3d308b76de98b43eb7400c8955c4deec9daa70caa0e00f87d13d38cdf040f80d; PATH_entries=37; exit=0; EXPECT matched; 43 passed, 0 failed, 0 skipped; captured output SHA256=be320a6315cd89f02f353274797a2c12e53d0fc8f17ec5eb006ec99c47afd15c; bytes=185

- [x] G2: The Windows application builds with the corrected shutdown, browser and dock flows.
  CHECK: DOTNET_CLI_HOME=/tmp/driftdeck-build-home NUGET_PACKAGES=/tmp/driftdeck-nuget /tmp/driftdeck-dotnet/dotnet build DriftDeck.slnx -p:EnableWindowsTargeting=true --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false
  EXPECT: Build succeeded.
  EVIDENCE: shell=/bin/zsh; cwd=/Users/davidmartinez/GitHub/3. Computer Apps/2. In Progress/DriftDeck; PATH_SHA256=3d308b76de98b43eb7400c8955c4deec9daa70caa0e00f87d13d38cdf040f80d; PATH_entries=37; exit=0; EXPECT matched; full solution built; 0 warnings, 0 errors; captured output SHA256=4e7f835151224877dcaf8434504c8b886b9f9f6457090d2f2ffc8ad738926134; bytes=585

- [ ] G3: Windows runtime verification confirms sign-in survives restart, final edits survive immediate close, and dock controls work at normal and high DPI.
  EVIDENCE: No Windows desktop/runtime is available on this macOS host. Manual checks are listed in docs/SESSION-VERIFICATION.md.

ABANDON: G3 Windows runtime and visual verification require a Windows machine; handoff is documented in docs/SESSION-VERIFICATION.md. No sign-in persistence or visual pass is claimed.
