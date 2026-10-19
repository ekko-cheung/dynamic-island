<#
.SYNOPSIS
  Publishes Dynamic Island as a self-contained single-file build and installs it to D:\tools\dynamic-island.

.PARAMETER Destination
  Install folder. Defaults to D:\tools\dynamic-island.

.PARAMETER NoStart
  Do not launch the app after publishing.

.NOTES
  This is the plain (unpackaged) build: media, Claude status, privacy indicators, timers and the local API work.
  Reading system notifications needs a package identity; use scripts\package.ps1 for that.
#>
param(
    [string]$Destination = 'D:\tools\dynamic-island',
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\DynamicIsland\DynamicIsland.csproj'
$staging = Join-Path $root 'artifacts\publish'

# Build into a staging folder first so a failed build never leaves the install half-replaced.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $staging
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# The running exe is locked, so stop it before overwriting.
Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process DynamicIsland -ErrorAction SilentlyContinue | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $Destination | Out-Null
Copy-Item (Join-Path $staging '*') $Destination -Recurse -Force
Write-Host "Installed to $Destination"

if (-not $NoStart) {
    Start-Process (Join-Path $Destination 'DynamicIsland.exe')
    Write-Host 'Started.'
}
