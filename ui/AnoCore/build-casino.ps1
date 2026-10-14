param(
    [Parameter(Mandatory = $true)]
    [string]$Cs2,
    [ValidatePattern('^[a-z][a-z0-9_]*$')]
    [string]$Addon = "anocasino_ui"
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
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
Compile-Resource "$style\ano_arcade.css" "$style\ano_arcade.vcss_c"
Compile-Resource "$layout\ano_arcade.xml" "$layout\ano_arcade.vxml_c"


Compile-Resource "$style\ano_casino.css" "$style\ano_casino.vcss_c"
Compile-Resource "$layout\ano_casino.xml" "$layout\ano_casino.vxml_c"
Compile-Resource "$style\ano_casino_native_v2.css" "$style\ano_casino_native_v2.vcss_c"
Compile-Resource "$layout\ano_casino_native_v2.xml" "$layout\ano_casino_native_v2.vxml_c"
foreach ($view in @("ano_casino_lobby", "ano_casino_slots", "ano_casino_coinflip", "ano_casino_roulette", "ano_casino_rounds")) {
    Compile-Resource "$style\$view.css" "$style\$view.vcss_c"
    Compile-Resource "$layout\$view.xml" "$layout\$view.vxml_c"
}

Copy-Item (Join-Path $src "$style\anomeme_banner.png") (Join-Path $content "$style\anomeme_banner.png") -Force
Compile-Resource "$style\anomeme_banner.vtex" "$style\anomeme_banner.vtex_c"

Write-Output "Casino resources compiled: $game"
Write-Output "Publish '$Addon' with Workshop Tools and mount it alongside anomeme_ui."
Write-Output "Use exactly one provider for ano_arcade resources: separate casino addon OR the combined build."
