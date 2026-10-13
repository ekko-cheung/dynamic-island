<#
.SYNOPSIS
  Builds the packaged layout of Dynamic Island and registers or packs it.

.PARAMETER Register
  Register the layout as a loose development package (needs Developer Mode, no signing).
  This gives the app a package identity, which UserNotificationListener and StartupTask require.

.PARAMETER Msix
  Pack artifacts\DynamicIsland.msix and sign it with a self-signed dev certificate
  (artifacts\DynamicIsland-dev.cer; trust it in "Trusted People" before installing).
#>
param(
    [switch]$Register,
    [switch]$Msix
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\DynamicIsland\DynamicIsland.csproj'
$layout = Join-Path $root 'artifacts\layout'
$manifest = Join-Path $root 'src\DynamicIsland\Package.appxmanifest'

function Find-SdkTool([string]$name) {
    $roots = @("$env:ProgramFiles(x86)\Windows Kits\10\bin", 'D:\Windows Kits\10\bin', 'C:\Program Files (x86)\Windows Kits\10\bin')
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $hit = Get-ChildItem $r -Recurse -Filter $name -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw "$name not found. Install the Windows 10/11 SDK."
}

# The exe locks its own files while running.
Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained false -o $layout
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item $manifest (Join-Path $layout 'AppxManifest.xml') -Force
Copy-Item (Join-Path $root 'src\DynamicIsland\Assets') (Join-Path $layout 'Assets') -Recurse -Force
Write-Host "Layout ready: $layout"

if ($Register) {
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host 'Registered. Start it from the Start menu ("Dynamic Island") or: explorer shell:AppsFolder\<PackageFamilyName>!DynamicIsland'
}

if ($Msix) {
    $makeappx = Find-SdkTool 'makeappx.exe'
    $signtool = Find-SdkTool 'signtool.exe'
    $out = Join-Path $root 'artifacts\DynamicIsland.msix'
    if (Test-Path $out) { Remove-Item $out -Force }
    & $makeappx pack /d $layout /p $out /nv
    if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

    $subject = 'CN=ekko-cheung' # must equal Identity/@Publisher in Package.appxmanifest
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.FriendlyName -eq 'DynamicIsland Dev' } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
            -FriendlyName 'DynamicIsland Dev' -CertStoreLocation Cert:\CurrentUser\My `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    Export-Certificate -Cert $cert -FilePath (Join-Path $root 'artifacts\DynamicIsland-dev.cer') | Out-Null
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $out
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
    Write-Host "Packed and signed: $out"
}
