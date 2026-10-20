param(
    [string]$Cs2 = "",
    [string]$Addon = "anomeme_ui",
    [string]$BiggestBoyWildPng = "",
    [string]$BookCoconutPng = "",
    [switch]$InstallLocalClient
)
$ErrorActionPreference = "Stop"
$arguments = @{ Addon = $Addon; IncludeBiggestBoy = $true; RequireBookCoconut = $true }
if ($Cs2) { $arguments.Cs2 = $Cs2 }
if ($BiggestBoyWildPng) { $arguments.BiggestBoyWildPng = $BiggestBoyWildPng }
if ($BookCoconutPng) { $arguments.BookCoconutPng = $BookCoconutPng }
if ($InstallLocalClient) { $arguments.InstallLocalClient = $true }
& (Join-Path $PSScriptRoot "build.ps1") @arguments
