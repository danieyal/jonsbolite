<#
Registers JonsboLite as a hidden logon task running with highest privileges.

This is the same mechanism the official app uses (see the *.xml task definitions in
resources/main): a Scheduled Task with RunLevel=HighestAvailable triggered at logon.
It is what gets LibreHardwareMonitor the ring-0 access it needs for CPU package
temp/power and SuperIO fan tachometers, without a UAC prompt every boot.

  .\install-autostart.ps1              install (must be run elevated)
  .\install-autostart.ps1 -Uninstall   remove the task

Run this from an elevated PowerShell.
#>
[CmdletBinding()]
param(
  [switch] $Uninstall,
  [string] $TaskName = 'JonsboLite',
  [string] $ExePath
)

$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
  [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
  Write-Error "This script must be run from an elevated PowerShell (Run as Administrator)."
  exit 1
}

if ($Uninstall) {
  if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "removed scheduled task '$TaskName'"
  } else {
    Write-Host "no scheduled task named '$TaskName'"
  }
  return
}

if (-not $ExePath) {
  $candidates = @(
    (Join-Path $PSScriptRoot 'JonsboLite.exe'),
    (Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\publish\JonsboLite.exe'),
    (Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\JonsboLite.exe')
  )
  $ExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $ExePath -or -not (Test-Path $ExePath)) {
  Write-Error "Could not find JonsboLite.exe. Publish it first, or pass -ExePath."
  exit 1
}
$ExePath = (Resolve-Path $ExePath).Path

$action    = New-ScheduledTaskAction -Execute $ExePath -Argument '--quiet' -WorkingDirectory (Split-Path $ExePath)
$trigger   = New-ScheduledTaskTrigger -AtLogOn
$trigger.Delay = 'PT3S'
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) `
                                        -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                                          -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
                                          -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
                       -Principal $principal -Settings $settings -Force | Out-Null

Write-Host "installed scheduled task '$TaskName'"
Write-Host "  exe : $ExePath"
Write-Host "  runs at logon (3s delay), elevated, hidden"
Write-Host ""
Write-Host "Start it now with:  Start-ScheduledTask -TaskName $TaskName"
