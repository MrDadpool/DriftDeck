[CmdletBinding()]
param(
    # The World of Warcraft install folder, the one holding Data and .build.info.
    [Parameter(Mandatory)]
    [string]$GameFolder,

    # Battle.net product code: wowf for World of Warcraft: Forever, wow for retail.
    [string]$Product = 'wowf',

    [string]$Locale = 'enUS'
)

# Pulls the fonts and interface textures the game theme names out of a local World of Warcraft
# install and lays them out under %LOCALAPPDATA%\DriftDeck\theme\game, the same paths the
# preset in src/DriftDeck/Themes/game.theme.json uses.
#
# Needed because the Forever client ships its fonts precompiled as .slug files and has no
# BlizzardInterfaceArt folder until someone runs exportInterfaceFiles in the game console. This
# reads the game's own storage instead, with CASCConsole from WoW-Tools/CASCExplorer. Nothing is
# written into the game folder and nothing enters this repository. Run it again after a patch.

$ErrorActionPreference = 'Stop'

# Keep in step with a CASCExplorer release that reads the product above.
$cascVersion = '1.0.240'
$cascUrl = "https://github.com/WoW-Tools/CASCExplorer/releases/download/CASCExplorer-v$cascVersion/CASCConsole.zip"

# File data IDs are stable across patches, so the files are named by ID rather than by
# downloading the two-million-line community listfile to look them up.
$files = [ordered]@{
    615960 = 'Fonts/FRIZQT__.TTF'
    615962 = 'Fonts/MORPHEUS.TTF'
    130828 = 'BlizzardInterfaceArt/Interface/Buttons/UI-Panel-Button-Up.blp'
    130825 = 'BlizzardInterfaceArt/Interface/Buttons/UI-Panel-Button-Down.blp'
    130755 = 'BlizzardInterfaceArt/Interface/Buttons/UI-CheckBox-Up.blp'
    130751 = 'BlizzardInterfaceArt/Interface/Buttons/UI-CheckBox-Check.blp'
    131076 = 'BlizzardInterfaceArt/Interface/DialogFrame/UI-DialogBox-Gold-Border.blp'
}

if (-not (Test-Path -LiteralPath (Join-Path $GameFolder '.build.info'))) {
    throw "$GameFolder has no .build.info. Pass the install folder, for example 'F:\Battle.net\World of Warcraft'."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $repositoryRoot "artifacts\tools\casc-$cascVersion"
$themeDirectory = Join-Path $env:LOCALAPPDATA 'DriftDeck\theme'
$gameArtDirectory = Join-Path $themeDirectory 'game'
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "driftdeck-game-art-$PID"

$console = Join-Path $toolsDirectory 'CASCConsole.exe'
if (-not (Test-Path -LiteralPath $console)) {
    New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
    $zip = Join-Path $toolsDirectory 'CASCConsole.zip'
    Invoke-WebRequest -Uri $cascUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -LiteralPath $zip -DestinationPath $toolsDirectory -Force
    Remove-Item -LiteralPath $zip
}

New-Item -ItemType Directory -Force -Path $workDirectory | Out-Null
try {
    # CASCConsole reads the listfile from its working directory.
    $listfile = Join-Path $workDirectory 'listfile.csv'
    $files.GetEnumerator() | ForEach-Object { "$($_.Key);extracted/$($_.Key)" } |
        Set-Content -LiteralPath $listfile -Encoding ascii

    Push-Location $workDirectory
    try {
        & $console -m Listfile -e listfile.csv -d out -l $Locale -p $Product -s $GameFolder
        if ($LASTEXITCODE -ne 0) {
            throw "CASCConsole exited with $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    foreach ($entry in $files.GetEnumerator()) {
        $source = Join-Path $workDirectory "out\extracted\$($entry.Key)"
        if (-not (Test-Path -LiteralPath $source)) {
            Write-Warning "File $($entry.Key) ($($entry.Value)) was not in the game data; that theme slot keeps its built-in look."
            continue
        }
        $destination = Join-Path $gameArtDirectory $entry.Value
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}
finally {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

# Point the theme at the extracted folder. An existing theme keeps every edit except its root;
# without one, the bundled preset is written. The file carries comments, which
# ConvertFrom-Json in Windows PowerShell rejects, so root is replaced as text.
$themeFile = Join-Path $themeDirectory 'theme.json'
$root = $gameArtDirectory.Replace('\', '/')
if (Test-Path -LiteralPath $themeFile) {
    $theme = Get-Content -LiteralPath $themeFile -Raw
} else {
    $theme = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\DriftDeck\Themes\game.theme.json') -Raw
}
$theme = [regex]::Replace($theme, '"root"\s*:\s*"[^"]*"', "`"root`": `"$root`"")
$theme = [regex]::Replace($theme, '"enabled"\s*:\s*false', '"enabled": true')
[IO.File]::WriteAllText($themeFile, $theme)

Write-Host "Game art is in $gameArtDirectory. In DriftDeck, open Settings and press Reload under the theme, or restart it."
