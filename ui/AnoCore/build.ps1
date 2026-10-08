param(
    [string]$Cs2 = "",
    [string]$Addon = "anomeme_ui",
    [string]$PreviewSource = "",
    [switch]$InstallLocalClient
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

# Custom HUDs expose class changes, not dynamic Image.src or JavaScript.
# Prepare genuine Workshop previews offline, then compile them into the addon.
$previewCss = Join-Path $src "styles\custom_game\anocore\ano_veto_previews.css"
if ($PreviewSource) {
    $PreviewSource = (Resolve-Path $PreviewSource).Path
    $previewCss = Join-Path $PreviewSource "ano_veto_previews.css"
    if (-not (Test-Path $previewCss) -or -not (Test-Path (Join-Path $PreviewSource "manifest.json"))) {
        throw "PreviewSource must be an output directory from prepare_veto_previews.py."
    }
    $previewDir = Join-Path $styleDir "previews"
    New-Item -ItemType Directory -Force -Path $previewDir | Out-Null
    foreach ($asset in Get-ChildItem (Join-Path $PreviewSource "previews") -File) {
        if ($asset.Extension -in @(".png", ".vtex")) {
            Copy-Item $asset.FullName (Join-Path $previewDir $asset.Name) -Force
        }
    }
    foreach ($texture in Get-ChildItem (Join-Path $PreviewSource "previews") -Filter "*.vtex" -File) {
        & $compiler -i (Join-Path $previewDir $texture.Name) -f -r
        if ($LASTEXITCODE -ne 0) { throw "Failed to compile map preview $($texture.Name)" }
        $compiled = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\anocore\previews\$($texture.BaseName).vtex_c"
        if (-not (Test-Path $compiled)) { throw "Compiled map preview missing: $compiled" }
        if ($InstallLocalClient) {
            $clientPreviewDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore\previews"
            New-Item -ItemType Directory -Force -Path $clientPreviewDir | Out-Null
            Copy-Item $compiled (Join-Path $clientPreviewDir "$($texture.BaseName).vtex_c") -Force
        }
    }
}
$previewStyleTarget = Join-Path $styleDir "ano_veto_previews.css"
Copy-Item $previewCss $previewStyleTarget -Force
& $compiler -i $previewStyleTarget -f -r
if ($LASTEXITCODE -ne 0) { throw "Failed to compile map preview stylesheet" }
$compiledPreviewStyle = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\anocore\ano_veto_previews.vcss_c"
if (-not (Test-Path $compiledPreviewStyle)) { throw "Compiled map preview stylesheet missing" }
if ($InstallLocalClient) {
    $clientStyleDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore"
    New-Item -ItemType Directory -Force -Path $clientStyleDir | Out-Null
    Copy-Item $compiledPreviewStyle (Join-Path $clientStyleDir "ano_veto_previews.vcss_c") -Force
}

foreach ($asset in @("anomeme_banner.png", "anomeme_banner.vtex")) {
    Copy-Item (Join-Path $src "styles\custom_game\anocore\$asset") (Join-Path $styleDir $asset) -Force
}
& $compiler -i (Join-Path $styleDir "anomeme_banner.vtex") -f -r
if ($LASTEXITCODE -ne 0) { throw "Failed to compile ANOMEME logo" }
$gameRoot = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama"
$logo = Join-Path $gameRoot "styles\custom_game\anocore\anomeme_banner.vtex_c"
if (-not (Test-Path $logo)) { throw "Compiled ANOMEME logo missing: $logo" }
if ($InstallLocalClient) {
    $clientStyleDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore"
    New-Item -ItemType Directory -Force -Path $clientStyleDir | Out-Null
    Copy-Item $logo (Join-Path $clientStyleDir "anomeme_banner.vtex_c") -Force
}

foreach ($name in @("ano_veto", "menu")) {
    $layoutTarget = Join-Path $layoutDir "$name.xml"
    $styleTarget = Join-Path $styleDir "$name.css"
    Copy-Item (Join-Path $src "layout\custom_game\anocore\$name.xml") $layoutTarget -Force
    Copy-Item (Join-Path $src "styles\custom_game\anocore\$name.css") $styleTarget -Force

    & $compiler -i $styleTarget -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $name.css" }
    & $compiler -i $layoutTarget -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $name.xml" }

    $gameRoot = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama"
    $outLayout = Join-Path $gameRoot "layout\custom_game\anocore\$name.vxml_c"
    $outStyle = Join-Path $gameRoot "styles\custom_game\anocore\$name.vcss_c"
    foreach ($required in @($outLayout, $outStyle)) {
        if (-not (Test-Path $required)) { throw "Expected compiled resource missing: $required" }
    }
    if ($InstallLocalClient) {
        $clientLayoutDir = Join-Path $Cs2 "game\csgo\panorama\layout\custom_game\anocore"
        $clientStyleDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore"
        New-Item -ItemType Directory -Force -Path $clientLayoutDir, $clientStyleDir | Out-Null
        Copy-Item $outLayout (Join-Path $clientLayoutDir "$name.vxml_c") -Force
        Copy-Item $outStyle (Join-Path $clientStyleDir "$name.vcss_c") -Force
    }
}

Write-Output "AnoCore Panorama HUD compiled successfully."
Write-Output "Compiled addon root: $gameRoot"
if (-not $InstallLocalClient) {
    Write-Output "For a one-client development test, rerun with -InstallLocalClient."
}
Write-Output "For normal players publish '$Addon' as a Workshop addon and deliver it to clients (for example with MultiAddonManager)."
