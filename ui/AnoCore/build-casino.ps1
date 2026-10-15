param(
    [Parameter(Mandatory = $true)]
    [string]$Cs2,
    [string]$BiggestBoyWildPng = "",
    [string]$BookCoconutPng = "",
    [switch]$RequireBookCoconut,
    [switch]$IncludeBiggestBoy,
    [ValidatePattern('^[a-z][a-z0-9_]*$')]
    [string]$Addon = "anocasino_ui"
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
    throw "CS2 Workshop Tools missing. Pass -Cs2 '<path-to-CS2>'."
}
$content = Join-Path $Cs2 "content\csgo_addons\$Addon\panorama"
$game = Join-Path $Cs2 "game\csgo_addons\$Addon\panorama"
$layout = "layout\custom_game\anocore"
$style = "styles\custom_game\anocore"
New-Item -ItemType Directory -Force -Path (Join-Path $content $layout), (Join-Path $content "$style\arcade") | Out-Null

function Compile-Resource([string]$RelativePath, [string]$OutputPath) {
    $target = Join-Path $content $RelativePath
    Copy-Item (Join-Path $src $RelativePath) $target -Force
    $compiledOutput = Join-Path $game $OutputPath
    if (Test-Path $compiledOutput -PathType Leaf) { Remove-Item -LiteralPath $compiledOutput -Force }
    & $compiler -i $target -f -r
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $RelativePath" }
    if (-not (Test-Path (Join-Path $game $OutputPath))) {
        throw "Compiled resource missing: $OutputPath"
    }
}

foreach ($symbol in @("cherry", "lemon", "plum", "orange", "grapes", "watermelon", "bell", "seven")) {
    Copy-Item (Join-Path $src "$style\arcade\$symbol.png") (Join-Path $content "$style\arcade\$symbol.png") -Force
    Compile-Resource "$style\arcade\$symbol.vtex" "$style\arcade\$symbol.vtex_c"
}
New-Item -ItemType Directory -Force -Path (Join-Path $content "$style\casino") | Out-Null
$casinoTextures = @("slots", "coinflip", "coin_heads", "coin_tails", "roulette", "blackjack", "book", "risk", "wheel", "biggest_kebap", "biggest_beer", "biggest_cigarette", "biggest_butter", "biggest_shot", "biggest_funnel", "biggest_g", "biggest_o", "biggest_a", "biggest_t", "biggest_scatter")
if ($buildBiggestBoy) { $casinoTextures += "biggest_book" }
if ($coconutAvailable) { $casinoTextures += "book_coconut" }
foreach ($texture in $casinoTextures) {
    Copy-Item (Join-Path $src "$style\casino\$texture.png") (Join-Path $content "$style\casino\$texture.png") -Force
    Compile-Resource "$style\casino\$texture.vtex" "$style\casino\$texture.vtex_c"
}
Compile-Resource "$style\ano_arcade.css" "$style\ano_arcade.vcss_c"
Compile-Resource "$layout\ano_arcade.xml" "$layout\ano_arcade.vxml_c"


Compile-Resource "$style\ano_casino.css" "$style\ano_casino.vcss_c"
Compile-Resource "$layout\ano_casino.xml" "$layout\ano_casino.vxml_c"
Compile-Resource "$style\ano_casino_native_v2.css" "$style\ano_casino_native_v2.vcss_c"
Compile-Resource "$layout\ano_casino_native_v2.xml" "$layout\ano_casino_native_v2.vxml_c"

$casinoViews = @("ano_casino_lobby", "ano_casino_slots", "ano_casino_coinflip", "ano_casino_roulette", "ano_casino_rounds")
if ($buildBiggestBoy) { $casinoViews += "ano_casino_biggest" }
foreach ($view in $casinoViews) {
    Compile-Resource "$style\$view.css" "$style\$view.vcss_c"
    Compile-Resource "$layout\$view.xml" "$layout\$view.vxml_c"
}

Copy-Item (Join-Path $src "$style\anomeme_banner.png") (Join-Path $content "$style\anomeme_banner.png") -Force
Compile-Resource "$style\anomeme_banner.vtex" "$style\anomeme_banner.vtex_c"

Write-Output "Casino resources compiled: $game"
Write-Output "Publish '$Addon' with Workshop Tools and mount it alongside anomeme_ui."
Write-Output "Use exactly one provider for ano_arcade resources: separate casino addon OR the combined build."
