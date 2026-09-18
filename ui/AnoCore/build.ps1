param(
    [string]$Cs2 = "",
    [string]$Addon = "anomeme_ui"
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Cs2) {
    $roots = @("C:\Program Files (x86)\Steam")
    $vdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        $matches = Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches
        foreach ($line in $matches) {
            foreach ($match in $line.Matches) {
                $roots += $match.Groups[1].Value.Replace('\\', '\')
            }
        }
    }

    foreach ($root in $roots) {
        $candidate = Join-Path $root "steamapps\common\Counter-Strike Global Offensive"
        if (Test-Path $candidate) {
            $Cs2 = $candidate
            break
        }
    }
}

if (-not $Cs2 -or -not (Test-Path $Cs2)) {
    throw "Counter-Strike 2 was not found. Pass -Cs2 '<path-to-CS2>'."
}

$compiler = Join-Path $Cs2 "game\bin\win64\resourcecompiler.exe"
if (-not (Test-Path $compiler)) {
    throw "CS2 Workshop Tools are not installed (resourcecompiler.exe missing). Install them from CS2 Settings and retry."
}

$contentRoot = Join-Path $Cs2 "content\csgo_addons\$Addon"
$layoutDir = Join-Path $contentRoot "panorama\layout\custom_game\anocore"
$styleDir = Join-Path $contentRoot "panorama\styles\custom_game\anocore"
New-Item -ItemType Directory -Force -Path $layoutDir, $styleDir | Out-Null

$layoutSource = Join-Path $src "layout\custom_game\anocore\ano_veto.xml"
$styleSource = Join-Path $src "styles\custom_game\anocore\ano_veto.css"
$layoutTarget = Join-Path $layoutDir "ano_veto.xml"
$styleTarget = Join-Path $styleDir "ano_veto.css"

Copy-Item $layoutSource $layoutTarget -Force
Copy-Item $styleSource $styleTarget -Force

& $compiler -i $styleTarget -r
if ($LASTEXITCODE -ne 0) { throw "Failed to compile ano_veto.css" }

& $compiler -i $layoutTarget -r
if ($LASTEXITCODE -ne 0) { throw "Failed to compile ano_veto.xml" }

$gameRoot = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama"
$outLayout = Join-Path $gameRoot "layout\custom_game\anocore\ano_veto.vxml_c"
$outStyle = Join-Path $gameRoot "styles\custom_game\anocore\ano_veto.vcss_c"

foreach ($required in @($outLayout, $outStyle)) {
    if (-not (Test-Path $required)) {
        throw "Expected compiled resource missing: $required"
    }
}

Write-Output "AnoCore Panorama HUD compiled successfully."
Write-Output "Compiled addon root: $gameRoot"
Write-Output "For local testing copy/mount the compiled addon for the client."
Write-Output "For normal players publish '$Addon' as a Workshop addon and mount it through your server addon delivery."
