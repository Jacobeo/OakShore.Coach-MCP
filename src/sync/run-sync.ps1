<#
.SYNOPSIS
The one way the sync is started, by the scheduler and by hand alike.

.DESCRIPTION
The scheduled task runs this script with no arguments; so does the athlete after
recording an Activity. There is one entry point so that a manual run cannot differ
from a scheduled one in some way nobody notices until a scheduled run fails.

A run with no arguments is driven by the Watermark and is bounded, so it is safe at
any time of day: with nothing new to fetch it posts nothing and changes nothing. Any
arguments given are passed through to sync_activities.py, which is how a date range
or a different ceiling is asked for.

Settings come from the user's own environment variables, not from this script and not
from the scheduled task, so no secret is written into either. See the README.

.EXAMPLE
.\run-sync.ps1
Catches up on whatever the watermark does not cover.

.EXAMPLE
.\run-sync.ps1 --from-date 2026-10-01 --to-date 2026-10-07
Re-syncs an explicit range, completed days included.
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $SyncArguments
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# A virtual environment beside the sync is preferred over whatever `python` resolves
# to, so a scheduled run uses the same interpreter the athlete installed into.
$venv = Join-Path $here '.venv\Scripts\python.exe'
$python = if (Test-Path $venv) { $venv } else { 'python' }

& $python (Join-Path $here 'sync_activities.py') @SyncArguments
exit $LASTEXITCODE
