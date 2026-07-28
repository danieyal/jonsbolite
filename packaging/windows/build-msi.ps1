<#
Publishes the Windows binary and packages it as an MSI.

  .\packaging\windows\build-msi.ps1                 # version from the csproj
  .\packaging\windows\build-msi.ps1 -Version 1.2.0

Needs the WiX CLI:  dotnet tool install --global wix
Output lands in dist\.
#>
[CmdletBinding()]
param(
  [string] $Version,
  [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (-not $Version) {
  [xml]$proj = Get-Content (Join-Path $repo 'JonsboLite\JonsboLite.csproj')
  $Version = ($proj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
  if (-not $Version) { $Version = '1.0.0' }
}

# MSI ProductVersion only honours major.minor.build; a 4th field is ignored for upgrade
# detection, and anything non-numeric is rejected outright.
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
  throw "Version must be major.minor.patch for an MSI (got '$Version'). Strip any suffix."
}

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
  throw "WiX CLI not found. Install it with:  dotnet tool install --global wix"
}

$publish = Join-Path $repo "JonsboLite\bin\$Configuration\net8.0-windows\win-x64\publish"
$dist    = Join-Path $repo 'dist'

Write-Host "==> publishing $Configuration win-x64"
& dotnet publish (Join-Path $repo 'JonsboLite\JonsboLite.csproj') `
    -f net8.0-windows -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

if (-not (Test-Path (Join-Path $publish 'JonsboLite.exe'))) {
  throw "expected JonsboLite.exe in $publish"
}

# A stale config from a dev run must not get packaged into the installer.
Remove-Item (Join-Path $publish 'jonsbolite.json') -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $dist | Out-Null
$msi = Join-Path $dist "JonsboLite-$Version-win-x64.msi"

Write-Host "==> building $([IO.Path]::GetFileName($msi))"
& wix build (Join-Path $PSScriptRoot 'JonsboLite.wxs') `
    -d "PublishDir=$publish" `
    -d "Version=$Version" `
    -d "AutostartScript=$(Join-Path $repo 'JonsboLite\install-autostart.ps1')" `
    -d "NoticesFile=$(Join-Path $repo 'THIRD-PARTY-NOTICES.md')" `
    -d "LicenseFile=$(Join-Path $repo 'LICENSE')" `
    -arch x64 `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

# Portable zip alongside the MSI, for people who would rather not install anything.
$zip = Join-Path $dist "JonsboLite-$Version-win-x64.zip"
Write-Host "==> building $([IO.Path]::GetFileName($zip))"
$staging = Join-Path ([IO.Path]::GetTempPath()) "jonsbolite-zip-$([guid]::NewGuid())"
New-Item -ItemType Directory -Force $staging | Out-Null
try {
  Copy-Item (Join-Path $publish 'JonsboLite.exe') $staging
  Copy-Item (Join-Path $repo 'JonsboLite\install-autostart.ps1') $staging
  Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $staging
  Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $staging 'LICENSE.txt')
  Copy-Item (Join-Path $repo 'JonsboLite\README.md') $staging
  Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
} finally {
  Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}

Write-Host ""
Get-ChildItem $dist -Filter "JonsboLite-$Version-win-x64.*" |
  ForEach-Object { "{0,10:N0}  {1}" -f $_.Length, $_.Name }
