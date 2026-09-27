param(
    [Parameter(Mandatory = $true)][string]$AppUrl,
    [Parameter(Mandatory = $true)][decimal]$ExpectedWeightKg,
    [decimal]$WriteWeightKg = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($env:COACH_ACCESS_TOKEN)) {
    throw 'Set COACH_ACCESS_TOKEN to a valid bearer token for the deployed app.'
}
$baseUrl = $AppUrl.TrimEnd('/')
$health = Invoke-RestMethod -Uri "$baseUrl/health"
if ($health.status -ne 'ok') { throw '/health did not return ok.' }

function Assert-Unauthorized([string]$Path, [string]$Body) {
    try {
        Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$baseUrl$Path" `
            -ContentType 'application/json' -Body $Body | Out-Null
        throw "$Path accepted an unauthenticated request."
    }
    catch {
        if ($null -eq $_.Exception.Response -or
            [int]$_.Exception.Response.StatusCode -ne 401) { throw }
    }
}

Assert-Unauthorized '/ingest' '{"version":1,"athleteProfiles":[{"bodyWeightKg":80.0}]}'
Assert-Unauthorized '/mcp' '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'

function Invoke-McpTool([string]$Name, [object]$Arguments) {
    $payload = @{
        jsonrpc = '2.0'
        id = 1
        method = 'tools/call'
        params = @{ name = $Name; arguments = $Arguments }
    } | ConvertTo-Json -Depth 10 -Compress
    $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$baseUrl/mcp" `
        -Headers @{
            Authorization = "Bearer $env:COACH_ACCESS_TOKEN"
            Accept = 'application/json, text/event-stream'
            'MCP-Protocol-Version' = '2025-11-25'
        } -ContentType 'application/json' -Body $payload
    $body = $response.Content
    if ($response.Headers['Content-Type'] -like 'text/event-stream*') {
        $line = $body -split "`n" | Where-Object { $_ -like 'data: *' } | Select-Object -First 1
        if (-not $line) { throw 'MCP returned an event stream without data.' }
        $body = $line.Substring(6)
    }
    $result = $body | ConvertFrom-Json
    if ($result.PSObject.Properties['error'] -or
        ($result.result.PSObject.Properties['isError'] -and $result.result.isError)) {
        throw "MCP tool $Name failed: $body"
    }
    return $result.result.structuredContent
}

if ($WriteWeightKg -gt 0) {
    $ingestBody = @{
        version = 1
        athleteProfiles = @(@{ bodyWeightKg = $WriteWeightKg })
    } | ConvertTo-Json -Depth 5 -Compress
    $ingested = Invoke-RestMethod -Method Post -Uri "$baseUrl/ingest" `
        -Headers @{ Authorization = "Bearer $env:COACH_ACCESS_TOKEN" } `
        -ContentType 'application/json' -Body $ingestBody
    if ([decimal]$ingested.profile.bodyWeightKg -ne $WriteWeightKg -or -not $ingested.lastSyncedAt) {
        throw 'Authenticated ingest did not save a fresh AthleteProfile.'
    }
    $updated = Invoke-McpTool 'update_athlete_profile' @{ bodyWeightKg = $ExpectedWeightKg }
    if ([decimal]$updated.profile.bodyWeightKg -ne $ExpectedWeightKg) {
        throw 'MCP update did not save the expected body weight.'
    }
}

$read = Invoke-McpTool 'get_athlete_profile' @{}
if ([decimal]$read.profile.bodyWeightKg -ne $ExpectedWeightKg -or -not $read.lastSyncedAt) {
    throw 'MCP read did not return the expected AthleteProfile with freshness.'
}
Write-Host "Smoke check passed for $baseUrl; weight $ExpectedWeightKg kg, synced $($read.lastSyncedAt)."
