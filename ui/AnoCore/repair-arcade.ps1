param(
    [string]$Cs2 = "",
    [ValidatePattern('^[a-z][a-z0-9_]*$')]
    [string]$Addon = "anomeme_ui",
    [ValidateSet("InstallLocalClient", "BuildOnly", "UseWorkshop")]
    [string]$Mode = "InstallLocalClient"
)

$ErrorActionPreference = "Stop"
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Cs2) {
    $roots = @("C:\Program Files (x86)\Steam")
    $vdf = Join-Path $roots[0] "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        foreach ($line in (Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches)) {
            foreach ($match in $line.Matches) { $roots += $match.Groups[1].Value.Replace('\\', '\') }
        }
    }
    foreach ($root in $roots) {
        $candidate = Join-Path $root "steamapps\common\Counter-Strike Global Offensive"
        if (Test-Path $candidate) { $Cs2 = $candidate; break }
    }
}
if (-not $Cs2 -or -not (Test-Path $Cs2 -PathType Container)) {
    throw "CS2 nicht gefunden. -Cs2 '<CS2-Installationsordner>' angeben."
}
$Cs2 = (Resolve-Path $Cs2).Path
if ($Mode -ne "BuildOnly" -and (Get-Process -Name cs2 -ErrorAction SilentlyContinue)) {
    throw "CS2 zuerst komplett beenden, damit keine alten UI-Ressourcen im Speicher bleiben."
}

$layout = "layout/custom_game/anocore/ano_arcade.vxml_c"
$style = "styles/custom_game/anocore/ano_arcade.vcss_c"
$symbols = @("cherry", "lemon", "plum")
$resources = @($layout, $style) + @($symbols | ForEach-Object { "styles/custom_game/anocore/arcade/$_.vtex_c" })
$client = Join-Path $Cs2 "game/csgo/panorama"
$backup = Join-Path $Cs2 ("anocore-ui-backups/arcade-" + (Get-Date -Format "yyyyMMdd-HHmmss-fff") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8))

if ($Mode -eq "UseWorkshop") {
    $moved = @()
    try {
        foreach ($relative in $resources) {
            $file = Join-Path $client $relative
            if (Test-Path $file -PathType Leaf) {
                $saved = Join-Path $backup $relative
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saved) | Out-Null
                Move-Item -LiteralPath $file -Destination $saved
                $moved += $relative
            }
        }
    } catch {
        foreach ($relative in $moved) {
            Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination (Join-Path $client $relative) -Force
        }
        throw
    }
    Write-Output "Lokale Arcade-Overrides gesichert: $backup"
    Write-Output "Jetzt liefert nur das gemountete Workshop-Addon die Arcade-UI. CS2 neu starten und !anoarcadetest testen."
    exit 0
}

# Validate the source actually expected by the installed original probe.
[xml]$xml = Get-Content (Join-Path $src "layout/custom_game/anocore/ano_arcade.xml") -Raw -Encoding UTF8
$ids = @($xml.SelectNodes("//*[@id]") | ForEach-Object { $_.GetAttribute("id") })
foreach ($id in @("root", "account", "profile", "bank", "stake", "win", "status", "close", "slots", "load", "spin")) {
    if ($ids -notcontains "ano_arcade_$id") { throw "Arcade-Quelldatei unpassend: ano_arcade_$id fehlt. Kein Client-Update."
    }
}
$compiler = Join-Path $Cs2 "game/bin/win64/resourcecompiler.exe"
if (-not (Test-Path $compiler -PathType Leaf)) { throw "CS2 Workshop Tools fehlen: resourcecompiler.exe nicht gefunden. Kein Client-Update." }
$content = Join-Path $Cs2 "content/csgo_addons/$Addon/panorama"
$game = Join-Path $Cs2 "game/csgo_addons/$Addon/panorama"

function Compile-Fresh([string]$SourceRelative, [string]$OutputRelative) {
    $inputFile = Join-Path $content $SourceRelative
    $outputFile = Join-Path $game $OutputRelative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $inputFile) | Out-Null
    Copy-Item -LiteralPath (Join-Path $src $SourceRelative) -Destination $inputFile -Force
    # Existence alone previously allowed stale files to pass as a successful build.
    if (Test-Path $outputFile -PathType Leaf) { Remove-Item -LiteralPath $outputFile -Force }
    & $compiler -i $inputFile -f -r
    if ($LASTEXITCODE -ne 0) { throw "Kompilierung fehlgeschlagen: $SourceRelative. Kein Client-Update." }
    if (-not (Test-Path $outputFile -PathType Leaf) -or (Get-Item $outputFile).Length -eq 0) {
        throw "Frisch kompilierte Ressource fehlt: $OutputRelative. Kein Client-Update."
    }
}

foreach ($symbol in $symbols) {
    $relative = "styles/custom_game/anocore/arcade/$symbol.png"
    $target = Join-Path $content $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $src $relative) -Destination $target -Force
    Compile-Fresh "styles/custom_game/anocore/arcade/$symbol.vtex" "styles/custom_game/anocore/arcade/$symbol.vtex_c"
}
Compile-Fresh "styles/custom_game/anocore/ano_arcade.css" $style
Compile-Fresh "layout/custom_game/anocore/ano_arcade.xml" $layout

$manifest = @($resources | ForEach-Object {
    [ordered]@{ Resource = $_; SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $game $_)).Hash }
})
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $backup "manifest.json") -Encoding UTF8

if ($Mode -eq "InstallLocalClient") {
    # Stage every output before replacing anything; keep exact prior files for rollback.
    $originals = @()
    foreach ($relative in $resources) {
        $file = Join-Path $client $relative
        if (Test-Path $file -PathType Leaf) {
            $saved = Join-Path $backup $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saved) | Out-Null
            Copy-Item -LiteralPath $file -Destination $saved
            $originals += $relative
        }
    }
    try {
        foreach ($relative in $resources) {
            $file = Join-Path $client $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file) | Out-Null
            Copy-Item -LiteralPath (Join-Path $game $relative) -Destination $file -Force
            if ((Get-FileHash -LiteralPath $file).Hash -ne (Get-FileHash -LiteralPath (Join-Path $game $relative)).Hash) {
                throw "Client-Datei stimmt nicht mit dem Build ueberein: $relative"
            }
        }
    } catch {
        foreach ($relative in $resources) {
            $file = Join-Path $client $relative
            if ($originals -contains $relative) { Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination $file -Force }
            elseif (Test-Path $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force }
        }
        throw
    }
    Write-Output "Arcade-UI direkt im lokalen Client installiert; SHA256 stimmt fuer alle 5 Dateien."
    Write-Output "Sicherung und Build-Nachweis: $backup"
    Write-Output "CS2 starten, Server beitreten, !anoarcadetest. Erwartete sichtbare Kennung: ARCADE UI CHECK 2026-10-14"
    Write-Output "Kein Plugin-/Server-Update erforderlich. Dies prueft nur diesen Client; Workshop bleibt separat."
} else {
    Write-Output "Arcade-UI frisch gebaut: $game"
    Write-Output "Build-Nachweis: $backup/manifest.json"
    Write-Output "Workshop Manager: vorhandenes Addon '$Addon' aktualisieren und danach Client/Server neu starten."
}
