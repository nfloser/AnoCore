param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [string]$Cs2 = "",
    [string]$Addon = "anomeme_ui"
)

$ErrorActionPreference = "Stop"
$uiRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$prepare = Join-Path $uiRoot "prepare-spitze-assets.py"
$build = Join-Path $uiRoot "build.ps1"
$target = Join-Path $uiRoot "styles\\custom_game\\anocore\\casino"
$sourceFolder = (Resolve-Path -LiteralPath $Source -ErrorAction Stop).Path
$python = Get-Command py -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command python -ErrorAction SilentlyContinue }
if (-not $python) {
    throw "Python is missing. Install Python and Pillow (py -m pip install pillow)."
}

Write-Output "Converting original Alles Spitze artwork from $sourceFolder"
& $python.Source $prepare --source $sourceFolder --destination $target
if ($LASTEXITCODE -ne 0) { throw "Alles Spitze image preparation failed. Check all five original images and Pillow." }

$names = @("ggspray", "safarimesh", "neonrevolution", "gold", "knife")
foreach ($name in $names) {
    foreach ($ext in @(".png", ".vtex")) {
        $file = Join-Path $target "spitze_$name$ext"
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Required image or Valve descriptor missing: $file"
        }
    }
}

Write-Output "Compiling skin textures and Panorama panels with the existing combined ANOMEME addon..."
$args = @("-Addon", $Addon, "-RequireSpitzeSkins", "-InstallLocalClient")
if ($Cs2) { $args += @("-Cs2", $Cs2) }
& $build @args
if (-not $?) { throw "Valve Panorama compilation failed. Check the build output." }
Write-Output "Compiled Alles Spitze textures are now in the local addon and CS2 test client."
Write-Output "Important: fully restart CS2. The server-mounted Steam Workshop addon may supersede local client files."
Write-Output "For everyone to see the images, re-upload the updated '$Addon' from CS2 Workshop Tools after test approval."
