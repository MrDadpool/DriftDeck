[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$FrameworkDependent,

    # Overrides the version compiled into the executable. Release builds pass the tag here so
    # the running build reports the same version the update check compares against.
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\DriftDeck\DriftDeck.csproj'
$artifactsDirectory = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsDirectory 'DriftDeck-win-x64'
$archivePath = Join-Path $artifactsDirectory 'DriftDeck-win-x64.zip'

$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

$publishArguments = @(
    'publish', $projectPath,
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--output', $publishDirectory,
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

if ($Version) {
    $publishArguments += "-p:Version=$($Version.TrimStart('v'))"
}

if ($FrameworkDependent) {
    $publishArguments += @('--self-contained', 'false')
} else {
    $publishArguments += @(
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true'
    )
}

& $dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "DriftDeck publish failed with exit code $LASTEXITCODE."
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host "Portable folder: $publishDirectory"
Write-Host "Portable archive: $archivePath"

