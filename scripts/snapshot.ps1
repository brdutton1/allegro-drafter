<#
.SYNOPSIS
  Reads everything Allegro Drafter can see in the project open in Revit and saves it
  to one JSON file you can share (snapshots\<project>-<date>.json).
  Read-only: it changes nothing in the model.

.EXAMPLE
  .\scripts\snapshot.ps1
#>
param(
    [string] $OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'snapshots')
)

$ErrorActionPreference = 'Stop'
$configPath = Join-Path $env:APPDATA 'AllegroDrafter\mcp.json'
if (-not (Test-Path $configPath)) { throw 'No mcp.json. Start Revit with the Allegro Drafter add-in first.' }
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$endpoint = "http://localhost:$($config.port)/mcp"
$headers = @{ Authorization = "Bearer $($config.token)" }
$script:id = 0

function Invoke-Tool([string] $name, [hashtable] $arguments = @{}) {
    $script:id++
    $body = @{ jsonrpc = '2.0'; id = $script:id; method = 'tools/call'; params = @{ name = $name; arguments = $arguments } } |
        ConvertTo-Json -Depth 10 -Compress
    try {
        $reply = Invoke-RestMethod -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 120
    } catch {
        return [pscustomobject]@{ error = "Could not reach Revit: $($_.Exception.Message)" }
    }
    if ($reply.error) { return [pscustomobject]@{ error = $reply.error.message } }
    $text = ($reply.result.content | ForEach-Object { $_.text }) -join "`n"
    if ($reply.result.isError) { return [pscustomobject]@{ error = $text } }
    try { return $text | ConvertFrom-Json } catch { return $text }
}

Write-Host 'Reading the open Revit project (read-only)...'
$info = Invoke-Tool 'get_project_info'
if ($info.error) { throw "Revit didn't answer: $($info.error)" }

$snapshot = [ordered]@{
    takenUtc   = (Get-Date).ToUniversalTime().ToString('o')
    project    = $info
    standards  = Invoke-Tool 'get_standards'
    levels     = Invoke-Tool 'list_levels'
    sheets     = Invoke-Tool 'list_sheets'
    views      = Invoke-Tool 'list_views'
    counts     = Invoke-Tool 'count_elements'
    rooms      = Invoke-Tool 'list_rooms'
    schedules  = Invoke-Tool 'list_schedules'
    doors      = Invoke-Tool 'list_elements' @{ category = 'doors'; includeParameterList = $true }
    windows    = Invoke-Tool 'list_elements' @{ category = 'windows'; includeParameterList = $true }
    roomParams = Invoke-Tool 'list_elements' @{ category = 'rooms'; includeParameterList = $true }
    legends    = [ordered]@{}
}

$legendList = Invoke-Tool 'read_legend'
$snapshot.legendList = $legendList
if (-not $legendList.error) {
    foreach ($name in @($legendList.legends)) {
        if ($name) { $snapshot.legends[$name] = Invoke-Tool 'read_legend' @{ name = $name } }
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$title = ($info.title -replace '[\\/:*?"<>|]', '_')
$file = Join-Path $OutDir ("{0}-{1}.json" -f $title, (Get-Date -Format 'yyyyMMdd-HHmm'))
[System.IO.File]::WriteAllText($file, ($snapshot | ConvertTo-Json -Depth 30), (New-Object System.Text.UTF8Encoding $false))

$failed = @($snapshot.GetEnumerator() | Where-Object { $_.Value -is [psobject] -and $_.Value.error } | ForEach-Object { $_.Key })
Write-Host ''
Write-Host "Saved: $file"
if ($failed.Count -gt 0) {
    Write-Host "Not available in this add-in version (run git pull + install.ps1 to get them): $($failed -join ', ')"
}
Write-Host 'Send this file to Claude. It contains project data (client name, rooms), so share it only where you want it.'
