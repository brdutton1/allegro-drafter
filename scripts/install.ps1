<#
.SYNOPSIS
  Builds, tests and installs Allegro Drafter into Revit 2025 and/or 2026 for the current Windows user.

.EXAMPLE
  .\scripts\install.ps1                 # every installed Revit year (2025, 2026)
  .\scripts\install.ps1 -Years 2025     # just 2025
  .\scripts\install.ps1 -Uninstall      # remove it
#>
param(
    [string[]] $Years = @('2025', '2026'),
    [string] $Configuration = 'Release',
    [switch] $Uninstall,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    throw 'Close Revit first. It locks the add-in files while running.'
}

function Get-AddinsFolder([string] $year) { Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year" }

if ($Uninstall) {
    foreach ($year in $Years) {
        $addins = Get-AddinsFolder $year
        Remove-Item (Join-Path $addins 'AllegroDrafter.addin') -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $addins 'AllegroDrafter') -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Removed Allegro Drafter from Revit $year."
    }
    Write-Host "Your standards profile and audit log in $env:APPDATA\AllegroDrafter were left alone."
    return
}

# Revit installs the .NET runtime, so 'dotnet' exists even without the SDK. Check for an actual SDK.
$sdks = if (Get-Command dotnet -ErrorAction SilentlyContinue) { & dotnet --list-sdks 2>$null } else { @() }
if (-not ($sdks | Where-Object { $_ -match '^(8|9|10)\.' })) {
    throw @'
The .NET 8 SDK is not installed (Revit only installs the runtime, which can't build).
Install it with:   winget install Microsoft.DotNet.SDK.8
or download "SDK 8.0, Windows x64" from https://dotnet.microsoft.com/download/dotnet/8.0
Then close PowerShell, open a new one, and run this script again.
'@
}

if (-not $SkipTests) {
    Write-Host 'Running tests...'
    dotnet test (Join-Path $root 'tests\AllegroDrafter.Core.Tests') -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Not installing.' }
}

$installed = @()
foreach ($year in $Years) {
    $revitExe = "C:\Program Files\Autodesk\Revit $year\Revit.exe"
    if (-not (Test-Path $revitExe)) {
        Write-Host "Revit $year not found at $revitExe. Skipping."
        continue
    }

    Write-Host "Building for Revit $year..."
    dotnet build (Join-Path $root 'src\AllegroDrafter.Revit') -c $Configuration -p:RevitVersion=$year --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build for Revit $year failed." }

    $addins = Get-AddinsFolder $year
    $dest = Join-Path $addins 'AllegroDrafter'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    $out = Join-Path $root "src\AllegroDrafter.Revit\bin\$Configuration\R$year"
    Get-ChildItem $out -File | Where-Object Name -ne 'AllegroDrafter.addin' | Copy-Item -Destination $dest -Force
    Copy-Item (Join-Path $out 'AllegroDrafter.addin') $addins -Force

    dotnet publish (Join-Path $root 'src\AllegroDrafter.Bridge') -c $Configuration -r win-x64 --self-contained false -o $dest --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }

    $installed += $year
    Write-Host "Installed for Revit $year -> $dest"
}

if ($installed.Count -eq 0) { throw 'No supported Revit version found. Nothing installed.' }

$bridge = Join-Path (Get-AddinsFolder $installed[0]) 'AllegroDrafter\AllegroDrafter.Bridge.exe'
$escaped = $bridge.Replace('\', '\\')
Write-Host ''
Write-Host 'Done. Connect Claude:'
Write-Host ''
Write-Host '  Claude Desktop: Settings > Developer > Edit Config, then inside "mcpServers" add:'
Write-Host "    `"allegro-drafter`": { `"command`": `"$escaped`" }"
Write-Host ''
Write-Host '  Claude Code:'
Write-Host "    claude mcp add allegro-drafter -- `"$bridge`""
Write-Host ''
Write-Host 'Then start Revit, open the office template and click Allegro > Export Standards.'
