param(
    [string]$Cs2 = "",
    [ValidatePattern('^[a-z][a-z0-9_]*$')]
    [string]$Addon = "anomeme_ui",
    [switch]$InstallLocalClient
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Cs2) {
    $Cs2 = "C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive"
}
if (-not (Test-Path $Cs2)) { throw "Counter-Strike 2 was not found. Pass -Cs2 '<path-to-CS2>'." }

$compiler = Join-Path $Cs2 "game\bin\win64\resourcecompiler.exe"
if (-not (Test-Path $compiler)) { throw "CS2 Workshop Tools are not installed (resourcecompiler.exe missing)." }

$contentRoot = Join-Path $Cs2 "content\csgo_addons\$Addon"
$layoutDir = Join-Path $contentRoot "panorama\layout\custom_game\anocore"
$styleDir = Join-Path $contentRoot "panorama\styles\custom_game\anocore"
New-Item -ItemType Directory -Force -Path $layoutDir, $styleDir | Out-Null

$views = @(
    "ano_casino_lobby",
    "ano_casino_slots",
    "ano_casino_coinflip",
    "ano_casino_roulette",
    "ano_casino_rounds"
)

foreach ($name in $views) {
    $layoutTarget = Join-Path $layoutDir "$name.xml"
    $styleTarget = Join-Path $styleDir "$name.css"
    Copy-Item (Join-Path $src "layout\custom_game\anocore\$name.xml") $layoutTarget -Force
    Copy-Item (Join-Path $src "styles\custom_game\anocore\$name.css") $styleTarget -Force

    & $compiler -i $styleTarget -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $name.css" }
    & $compiler -i $layoutTarget -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $name.xml" }

    $outLayout = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\layout\custom_game\anocore\$name.vxml_c"
    $outStyle = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\anocore\$name.vcss_c"
    if (-not (Test-Path $outLayout) -or -not (Test-Path $outStyle)) {
        throw "Compiled Casino resource missing for $name"
    }

    if ($InstallLocalClient) {
        $clientLayout = Join-Path $Cs2 "game\csgo\panorama\layout\custom_game\anocore"
        $clientStyle = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore"
        New-Item -ItemType Directory -Force -Path $clientLayout, $clientStyle | Out-Null
        Copy-Item $outLayout (Join-Path $clientLayout "$name.vxml_c") -Force
        Copy-Item $outStyle (Join-Path $clientStyle "$name.vcss_c") -Force
    }
}

Write-Output "AnoCasino modular Panorama HUDs compiled successfully."
