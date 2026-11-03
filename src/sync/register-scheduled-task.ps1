<#
.SYNOPSIS
Registers, or re-registers, the daily Garmin sync on this machine.

.DESCRIPTION
Creates a Windows scheduled task that runs run-sync.ps1 once a day, starting a
missed occurrence as soon as the machine is next available. Re-running this script
replaces the existing task.

The task runs as the current user, interactive, so it inherits that user's
environment variables and reaches the saved sign-in and the Garmin token in the
user profile. No password is stored and no secret is written into the task. The
README's "Scheduled and manual runs" section covers the settings it expects.

.PARAMETER At
Time of day to run, as HH:mm. Evening, after the athlete usually trains.

.PARAMETER TaskName
Name of the scheduled task.

.PARAMETER Unregister
Removes the task instead of creating it.

.EXAMPLE
.\register-scheduled-task.ps1
Registers the sync for 21:30 daily.

.EXAMPLE
.\register-scheduled-task.ps1 -At 22:15
Registers it for a later evening slot.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d{2}:\d{2}$')]
    [string] $At = '21:30',
    [string] $TaskName = 'Coach Garmin sync',
    [switch] $Unregister
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$runner = Join-Path $here 'run-sync.ps1'

if (-not (Test-Path $runner)) {
    throw "The sync runner is missing at $runner."
}

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Removed the existing '$TaskName' task."
}

if ($Unregister) {
    Write-Host 'Nothing is scheduled now. Manual runs still work.'
    return
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$runner`""
$trigger = New-ScheduledTaskTrigger -Daily -At $At

# StartWhenAvailable runs a missed occurrence once the machine is back, which is what
# makes an evening schedule survive a machine that was off. The run is paced and can
# take many minutes, so no execution time limit is imposed.
#
# No restart on failure: a run aborts on 401, 403 or 429 and exits non-zero, and
# restarting it would contact Garmin again inside a block it was told to back off
# from, which is exactly what spec 03 and ADR 0005 forbid. A failed run waits for
# tomorrow, and the watermark means it loses nothing by waiting.
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -DontStopOnIdleEnd

$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal `
    -Description 'Syncs Garmin Connect into the Coach datastore. Catches up on missed days.' | Out-Null

Write-Host "Registered '$TaskName' to run daily at $At, catching up when the machine was off."
Write-Host "Run it now with: Start-ScheduledTask -TaskName '$TaskName'"
