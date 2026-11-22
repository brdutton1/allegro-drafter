<#
.SYNOPSIS
  Registers Allegro Drafter in Claude Desktop's config (claude_desktop_config.json).
  Keeps every other setting, backs the file up first, and handles both the
  regular installer and the Microsoft Store version of Claude Desktop.

.EXAMPLE
  .\scripts\connect-claude.ps1
#>
param(
    [string] $Year = ''
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'Claude' -ErrorAction SilentlyContinue) {
    throw 'Quit Claude Desktop first (right-click its tray icon > Quit). It can overwrite the config while running.'
}

# Find the installed bridge (newest Revit year first, unless one is given).
$years = if ($Year) { @($Year) } else { @('2026', '2025') }
$bridge = $null
foreach ($y in $years) {
    $candidate = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$y\AllegroDrafter\AllegroDrafter.Bridge.exe"
    if (Test-Path $candidate) { $bridge = $candidate; break }
}
if (-not $bridge) { throw 'AllegroDrafter.Bridge.exe not found. Run .\scripts\install.ps1 first.' }

# Claude Desktop keeps its config in one of two places depending on how it was installed.
$configs = @(Join-Path $env:APPDATA 'Claude\claude_desktop_config.json')
$store = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Directory -Filter 'Claude_*' -ErrorAction SilentlyContinue
foreach ($pkg in $store) {
    $configs += Join-Path $pkg.FullName 'LocalCache\Roaming\Claude\claude_desktop_config.json'
}
# Only write where Claude Desktop actually lives (its folder exists).
$targets = $configs | Where-Object { Test-Path (Split-Path $_ -Parent) }
if (-not $targets) {
    $targets = @($configs[0])
    New-Item -ItemType Directory -Force -Path (Split-Path $configs[0] -Parent) | Out-Null
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false

foreach ($path in $targets) {
    $config = $null
    if (Test-Path $path) {
        $raw = [System.IO.File]::ReadAllText($path)
        if ($raw.Trim().Length -gt 0) {
            try {
                $config = $raw | ConvertFrom-Json
            } catch {
                throw "Couldn't read $path (it isn't valid JSON). Fix or delete it, then run this again. Error: $($_.Exception.Message)"
            }
        }
        Copy-Item $path "$path.bak-$(Get-Date -Format yyyyMMdd-HHmmss)" -Force
    }
    if (-not $config) { $config = [pscustomobject]@{} }

    if (-not ($config.PSObject.Properties.Name -contains 'mcpServers') -or -not $config.mcpServers) {
        $config | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $entry = [pscustomobject]@{ command = $bridge }
    $config.mcpServers | Add-Member -NotePropertyName 'allegro-drafter' -NotePropertyValue $entry -Force

    # No BOM: a BOM at the start makes Claude Desktop reject the file.
    [System.IO.File]::WriteAllText($path, ($config | ConvertTo-Json -Depth 20), $utf8NoBom)
    Write-Host "Updated $path"
}

Write-Host ''
Write-Host "Allegro Drafter registered -> $bridge"
Write-Host 'Now open Claude Desktop. Allegro Drafter should show up in the tools menu of a new chat.'
