param(
    [string]$SourceDirectory = ""
)

# The five original skins may live next to the other AnoCasino PNGs.
# When present, convert them before the regular Valve resource compilation.
# Once the prepared spitze_*.png files are checked in, Python is not required.
$ErrorActionPreference = "Stop"
if (-not $SourceDirectory) {
    $SourceDirectory = Join-Path $PSScriptRoot "styles\custom_game\anocore\casino"
}

$requirements = [ordered]@{
    "GG Spray" = @("ggspray.png")
    "Safari Mesh" = @("safarimesh.webp", "safarimesh.png")
    "Neon Revolution" = @("neonrevolution.webp", "neonrevolution.png")
    "Gold" = @("gold.webp", "gold.png")
    "Knife" = @("bluegem.png", "knife.png", "messer.png")
}

$found = @()
$missing = @()
foreach ($entry in $requirements.GetEnumerator()) {
    $original = $null
    foreach ($candidate in $entry.Value) {
        $check = Join-Path $SourceDirectory $candidate
        if (Test-Path -LiteralPath $check -PathType Leaf) {
            $original = $check
            break
        }
    }
    if ($null -eq $original) {
        $missing += "$($entry.Key): $($entry.Value -join ' / ')"
    } else {
        $found += $original
    }
}

# Normal CI/build installs remain valid even before source artwork is added.
if ($found.Count -eq 0) { return }
if ($missing.Count -gt 0) {
    throw "Alles Spitze original skins are incomplete in '$SourceDirectory'. Missing: $($missing -join ', '). Add all five images or use prepared spitze_*.png files only."
}

$python = Get-Command py -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command python -ErrorAction SilentlyContinue }
if (-not $python) {
    throw "Original Alles Spitze skins found but Python is missing. Install Python, then run 'py -m pip install pillow'."
}
$converter = Join-Path $PSScriptRoot "prepare-spitze-assets.py"
Write-Output "Preparing all five Alles Spitze skins in $SourceDirectory"
& $python.Source $converter --source $SourceDirectory --destination $SourceDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Alles Spitze image conversion failed. Install Pillow with 'py -m pip install pillow' and retry."
}
