param(
    [string]$Cs2 = "",
    [string]$BiggestBoyWildPng = "",
    [string]$BookCoconutPng = "",
    [switch]$RequireBookCoconut,
    [switch]$IncludeBiggestBoy,
    [ValidatePattern('^[a-z][a-z0-9_]*$')]
    [string]$Addon = "anomeme_ui",
    [string]$PreviewSource = "",
    [ValidateRange(128, 4096)]
    [int]$MaxPreviewTextures = 1024,
    [switch]$ClearPreviews,
    [switch]$InstallLocalClient
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$wildTarget = Join-Path $src "styles\custom_game\anocore\casino\biggest_book.png"
$buildBiggestBoy = $IncludeBiggestBoy -or $BiggestBoyWildPng -or (Test-Path -LiteralPath $wildTarget -PathType Leaf)
if ($buildBiggestBoy) {
    if ($BiggestBoyWildPng) {
        $wildBytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $BiggestBoyWildPng).Path)
        if ($wildBytes.Length -lt 8 -or [BitConverter]::ToString($wildBytes, 0, 8) -ne "89-50-4E-47-0D-0A-1A-0A") {
            throw "BiggestBoyWildPng must be the original Biggest Boy Wild PNG."
        }
        if ([System.IO.Path]::GetFullPath($BiggestBoyWildPng) -ne [System.IO.Path]::GetFullPath($wildTarget)) {
            Copy-Item -LiteralPath $BiggestBoyWildPng -Destination $wildTarget -Force
        }
    }
    if (-not (Test-Path -LiteralPath $wildTarget -PathType Leaf)) {
        throw "Biggest Boy original Biggest Boy Wild PNG is missing: $wildTarget. Supply -BiggestBoyWildPng <original.png>. Build stopped before changing the addon."
    }
}



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

# Always compile the user's specific golden coconut asset for the Book wild/scatter.
# Check it before modifying an installed CS2 addon.
$coconutTarget = Join-Path $src "styles\\custom_game\\anocore\\casino\\book_coconut.png"
if ($BookCoconutPng) {
    $coconutSource = (Resolve-Path -LiteralPath $BookCoconutPng).Path
    $coconutBytes = [System.IO.File]::ReadAllBytes($coconutSource)
    if ($coconutBytes.Length -lt 8 -or [BitConverter]::ToString($coconutBytes, 0, 8) -ne "89-50-4E-47-0D-0A-1A-0A") {
        throw "BookCoconutPng must be a real PNG file."
    }
    if ([System.IO.Path]::GetFullPath($coconutSource) -ne [System.IO.Path]::GetFullPath($coconutTarget)) {
        Copy-Item -LiteralPath $coconutSource -Destination $coconutTarget -Force
    }
}
$coconutAvailable = Test-Path -LiteralPath $coconutTarget -PathType Leaf
if (-not $coconutAvailable -and $RequireBookCoconut) {
    throw "The original golden coconut PNG is missing: $coconutTarget. Provide -BookCoconutPng '<image.png>' before building."
}
if (-not $coconutAvailable) {
    Write-Warning "Book coconut PNG absent; dedicated Book UI texture will not be compiled. Use -RequireBookCoconut for the complete casino addon."
}

$compiler = Join-Path $Cs2 "game\bin\win64\resourcecompiler.exe"
if (-not (Test-Path $compiler)) {
    throw "CS2 Workshop Tools are not installed (resourcecompiler.exe missing). Install them from CS2 Settings and retry."
}

$contentRoot = Join-Path $Cs2 "content\csgo_addons\$Addon"
$layoutDir = Join-Path $contentRoot "panorama\layout\custom_game\anocore"
$styleDir = Join-Path $contentRoot "panorama\styles\custom_game\anocore"
New-Item -ItemType Directory -Force -Path $layoutDir, $styleDir | Out-Null

# Rebuilds retain existing prepared images. Only -ClearPreviews resets to neutral.
if ($PreviewSource -and $ClearPreviews) {
    throw "Use PreviewSource or ClearPreviews, not both."
}
$previewStyleTarget = Join-Path $styleDir "ano_veto_previews.css"
$previewCss = Join-Path $src "styles\custom_game\anocore\ano_veto_previews.css"
$previewAssets = Join-Path $styleDir "previews"
if ($PreviewSource) {
    $PreviewSource = (Resolve-Path $PreviewSource).Path
    $previewCss = Join-Path $PreviewSource "ano_veto_previews.css"
    $previewAssets = Join-Path $PreviewSource "previews"
    if (-not (Test-Path $previewCss) -or -not (Test-Path (Join-Path $PreviewSource "manifest.json"))) {
        throw "PreviewSource must be an output directory from prepare_veto_previews.py."
    }
} elseif (-not $ClearPreviews -and (Test-Path $previewStyleTarget)) {
    $previewCss = $previewStyleTarget
    Write-Output "Retaining existing map preview mapping from the addon content folder."
}

$previewText = Get-Content $previewCss -Raw
$recoverMapping = $false
if (-not $PreviewSource -and -not $ClearPreviews -and $previewText -notmatch '\.ano_preview_' -and (Test-Path $previewAssets)) {
    # Older builds could replace the CSS with a neutral file while leaving real
    # prepared PNG/VTEX sources intact. Recover only their exact generated keys.
    $preparedKeys = @(Get-ChildItem $previewAssets -Filter "*.vtex" -File |
        Where-Object { $_.BaseName -match '^ano_preview_(?:w_[0-9]+|m_[0-9a-f]{16})$' } |
        ForEach-Object { $_.BaseName } | Sort-Object -Unique)
    if ($preparedKeys.Count -gt 0) {
        $rules = @("/* Recovered from existing prepared addon textures. */")
        foreach ($key in $preparedKeys) {
            $rules += ".$key { background-image: url(`"s2r://panorama/styles/custom_game/anocore/previews/$key.vtex`"); }"
        }
        $previewText = ($rules -join "`n") + "`n"
        $recoverMapping = $true
        Write-Output "Recovering map preview mapping from existing prepared textures."
    }
}
$textureMatches = [regex]::Matches($previewText, 's2r://panorama/styles/custom_game/anocore/previews/(ano_preview_(?:w_[0-9]+|m_[0-9a-f]{16}))\.vtex')
$textureNames = @($textureMatches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
if ($textureNames.Count -gt $MaxPreviewTextures) {
    throw "Preview stylesheet contains $($textureNames.Count) textures; the build limit is $MaxPreviewTextures. Use -MaxPreviewTextures <count> (maximum 4096) for a larger retained addon catalog."
}
if ($previewText -match '\.ano_preview_' -and $textureNames.Count -eq 0) {
    throw "Preview mapping is invalid. Supply the original prepared directory with -PreviewSource."
}
# Check before replacing any mapping, so a failed rebuild cannot erase it.
foreach ($name in $textureNames) {
    foreach ($extension in @(".png", ".vtex")) {
        if (-not (Test-Path (Join-Path $previewAssets "$name$extension"))) {
            throw "Missing preview source $name$extension. Supply -PreviewSource with the original prepared directory, or explicitly use -ClearPreviews."
        }
    }
}
$previewDir = Join-Path $styleDir "previews"
if ($textureNames.Count -gt 0) { New-Item -ItemType Directory -Force -Path $previewDir | Out-Null }
foreach ($name in $textureNames) {
    foreach ($extension in @(".png", ".vtex")) {
        $asset = Join-Path $previewAssets "$name$extension"
        $target = Join-Path $previewDir "$name$extension"
        if ([IO.Path]::GetFullPath($asset) -ne [IO.Path]::GetFullPath($target)) {
            Copy-Item $asset $target -Force
        }
    }
    & $compiler -i (Join-Path $previewDir "$name.vtex") -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile map preview $name" }
    $compiled = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\anocore\previews\$name.vtex_c"
    if (-not (Test-Path $compiled)) { throw "Compiled map preview missing: $compiled" }
    if ($InstallLocalClient) {
        $clientPreviewDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore\previews"
        New-Item -ItemType Directory -Force -Path $clientPreviewDir | Out-Null
        Copy-Item $compiled (Join-Path $clientPreviewDir "$name.vtex_c") -Force
    }
}
Write-Output "Map preview textures: $($textureNames.Count)"
if ($recoverMapping) {
    Set-Content $previewStyleTarget $previewText -Encoding UTF8
} elseif ([IO.Path]::GetFullPath($previewCss) -ne [IO.Path]::GetFullPath($previewStyleTarget)) {
    Copy-Item $previewCss $previewStyleTarget -Force
}
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

# Compile replaceable arcade fruit textures from source PNG/VTEX pairs.
$arcadeDir = Join-Path $styleDir "arcade"
New-Item -ItemType Directory -Force -Path $arcadeDir | Out-Null
foreach ($symbol in @("cherry", "lemon", "plum", "orange", "grapes", "watermelon", "bell", "seven")) {
    foreach ($extension in @(".png", ".vtex")) {
        Copy-Item (Join-Path $src "styles\custom_game\anocore\arcade\$symbol$extension") (Join-Path $arcadeDir "$symbol$extension") -Force
    }
    $compiled = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\anocore\arcade\$symbol.vtex_c"
    if (Test-Path $compiled -PathType Leaf) { Remove-Item -LiteralPath $compiled -Force }
    & $compiler -i (Join-Path $arcadeDir "$symbol.vtex") -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile arcade symbol $symbol" }
    $compiled = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\anocore\arcade\$symbol.vtex_c"
    if (-not (Test-Path $compiled)) { throw "Compiled arcade symbol missing: $compiled" }
    if ($InstallLocalClient) {
        $clientArcadeDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore\arcade"
        New-Item -ItemType Directory -Force -Path $clientArcadeDir | Out-Null
        Copy-Item $compiled (Join-Path $clientArcadeDir "$symbol.vtex_c") -Force
    }
}

# Casino covers and exact numbered roulette wheel belong to the matching HUD build.
$casinoTextureDir = Join-Path $styleDir "casino"
New-Item -ItemType Directory -Force -Path $casinoTextureDir | Out-Null
$casinoTextures = @("slots", "coinflip", "coin_heads", "coin_tails", "roulette", "blackjack", "book", "risk", "wheel", "biggest_kebap", "biggest_beer", "biggest_cigarette", "biggest_butter", "biggest_shot", "biggest_funnel", "biggest_g", "biggest_o", "biggest_a", "biggest_t", "biggest_scatter")
if ($buildBiggestBoy) { $casinoTextures += "biggest_book" }
if ($coconutAvailable) { $casinoTextures += "book_coconut" }
# Dedicated Alles Spitze skins are user-supplied, so do not pretend absent artwork
# is already packaged. Keep the text-only HUD usable when a source image is missing.
$spitzeNames = @("ggspray", "safarimesh", "neonrevolution", "gold", "knife")
$spitzeMissing = @()
foreach ($name in $spitzeNames) {
    $texture = "spitze_$name"
    if (Test-Path -LiteralPath (Join-Path $src "styles\\custom_game\\anocore\\casino\\$texture.png") -PathType Leaf) {
        $casinoTextures += $texture
    } else {
        $spitzeMissing += $texture
    }
}
if ($spitzeMissing.Count -gt 0) {
    Write-Warning "Alles Spitze images absent: $($spitzeMissing -join ', '). Copy/convert all five skins before publishing the addon."
}

foreach ($texture in $casinoTextures) {
    foreach ($extension in @(".png", ".vtex")) {
        Copy-Item (Join-Path $src "styles\custom_game\anocore\casino\$texture$extension") (Join-Path $casinoTextureDir "$texture$extension") -Force
    }
    $compiled = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama\styles\custom_game\anocore\casino\$texture.vtex_c"
    if (Test-Path $compiled -PathType Leaf) { Remove-Item -LiteralPath $compiled -Force }
    & $compiler -i (Join-Path $casinoTextureDir "$texture.vtex") -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile casino texture $texture" }
    if (-not (Test-Path $compiled -PathType Leaf)) { throw "Compiled casino texture missing: $compiled" }
    if ($InstallLocalClient) {
        $clientCasinoDir = Join-Path $Cs2 "game\csgo\panorama\styles\custom_game\anocore\casino"
        New-Item -ItemType Directory -Force -Path $clientCasinoDir | Out-Null
        Copy-Item $compiled (Join-Path $clientCasinoDir "$texture.vtex_c") -Force
    }
}

$casinoViews = @("ano_veto_cards", "ano_veto", "ano_veto_create", "menu", "ano_tournament", "ano_arcade", "ano_casino", "ano_casino_native_v2", "ano_casino_lobby", "ano_casino_slots", "ano_casino_coinflip", "ano_casino_roulette", "ano_casino_rounds", "ano_casino_spitze", "ano_casino_blackjack")
if ($buildBiggestBoy) { $casinoViews += "ano_casino_biggest" }
foreach ($name in $casinoViews) {
    $layoutTarget = Join-Path $layoutDir "$name.xml"
    $styleTarget = Join-Path $styleDir "$name.css"
    Copy-Item (Join-Path $src "layout\custom_game\anocore\$name.xml") $layoutTarget -Force
    Copy-Item (Join-Path $src "styles\custom_game\anocore\$name.css") $styleTarget -Force

    $gameRoot = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama"
    $outLayout = Join-Path $gameRoot "layout\custom_game\anocore\$name.vxml_c"
    $outStyle = Join-Path $gameRoot "styles\custom_game\anocore\$name.vcss_c"
    foreach ($compiledOutput in @($outLayout, $outStyle)) {
        if (Test-Path $compiledOutput -PathType Leaf) { Remove-Item -LiteralPath $compiledOutput -Force }
    }
    & $compiler -i $styleTarget -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $name.css" }
    & $compiler -i $layoutTarget -f -r
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile $name.xml" }

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
