[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Version stamped into the executable and the Velopack package. Release builds pass the tag
    # here. Defaults to the csproj version.
    [string]$Version,

    # Downloads the latest published release first, so Velopack can build a delta package
    # against it. Release builds pass this; local builds do not need it.
    [switch]$WithDelta
)

$ErrorActionPreference = 'Stop'

# Keep in step with the Velopack package version in src/DriftDeck/DriftDeck.csproj.
$vpkVersion = '1.2.161'
$packId = 'DriftDeck.App'
$repositoryUrl = 'https://github.com/MrDadpool/DriftDeck'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\DriftDeck\DriftDeck.csproj'
$artifactsDirectory = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsDirectory 'publish'
$releasesDirectory = Join-Path $artifactsDirectory 'releases'
$toolsDirectory = Join-Path $artifactsDirectory 'tools'

if (-not $Version) {
    $Version = ([xml](Get-Content -LiteralPath $projectPath)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart('v')

foreach ($directory in $publishDirectory, $releasesDirectory) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

# Self-contained but not single-file: Velopack's delta updates work per file, and a single
# bundled executable would make every update a full download.
& dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    "-p:Version=$Version" `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "DriftDeck publish failed with exit code $LASTEXITCODE." }

& dotnet tool update vpk --version $vpkVersion --tool-path $toolsDirectory
if ($LASTEXITCODE -ne 0) { throw "Installing vpk failed with exit code $LASTEXITCODE." }
$vpk = Join-Path $toolsDirectory 'vpk.exe'

if ($WithDelta) {
    $downloadArguments = @('download', 'github', '--repoUrl', $repositoryUrl, '--outputDir', $releasesDirectory)
    if ($env:GITHUB_TOKEN) { $downloadArguments += @('--token', $env:GITHUB_TOKEN) }
    & $vpk @downloadArguments
    if ($LASTEXITCODE -ne 0) { throw "Downloading the previous release failed with exit code $LASTEXITCODE." }
}

# WebView2 is bootstrapped by Setup rather than assumed: Windows 11 ships it, but a
# trimmed or older Windows 10 install may not, and every browser panel depends on it.
& $vpk pack `
    --packId $packId `
    --packVersion $Version `
    --packDir $publishDirectory `
    --packTitle 'DriftDeck' `
    --packAuthors 'DriftDeck contributors' `
    --mainExe 'DriftDeck.exe' `
    --framework webview2 `
    --noPortable `
    --outputDir $releasesDirectory
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE." }

Write-Host "Installer and update packages: $releasesDirectory"
