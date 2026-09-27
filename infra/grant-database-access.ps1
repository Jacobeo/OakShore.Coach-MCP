param(
    [Parameter(Mandatory = $true)][string]$ResourceGroup,
    [Parameter(Mandatory = $true)][string]$SqlServerName,
    [Parameter(Mandatory = $true)][string]$SqlServerFqdn,
    [Parameter(Mandatory = $true)][string]$AppIdentityName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($AppIdentityName -notmatch '^[a-z][a-z0-9-]{2,63}$') {
    throw 'AppIdentityName must contain only lowercase letters, digits, and hyphens.'
}

$accessToken = & az account get-access-token --resource 'https://database.windows.net/' --query accessToken --output tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($accessToken)) {
    throw 'Azure CLI could not obtain a SQL access token. Sign in with an account authorized as the SQL Entra administrator.'
}
$grantSql = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'grant-app.sql') -Raw).Replace('$(AppIdentityName)', $AppIdentityName)

function Invoke-AzJson([string[]]$Arguments) {
    $result = & az @Arguments --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: $($Arguments[0..([Math]::Min(2, $Arguments.Length - 1))] -join ' ')"
    }
    return $result | ConvertFrom-Json
}

$deploymentIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
if ($deploymentIp -notmatch '^(\d{1,3}\.){3}\d{1,3}$') {
    throw 'Could not determine an IPv4 address for the temporary SQL deployment firewall rule.'
}
$firewallRuleName = "CoachDeploy-$PID"
$firewallRuleCreated = $false
try {
    Invoke-AzJson -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $ResourceGroup, '--server', $SqlServerName,
        '--name', $firewallRuleName,
        '--start-ip-address', $deploymentIp,
        '--end-ip-address', $deploymentIp
    ) | Out-Null
    $firewallRuleCreated = $true

    $connection = [System.Data.SqlClient.SqlConnection]::new(
        "Server=tcp:$SqlServerFqdn,1433;Database=coach;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30")
    try {
        $connection.AccessToken = $accessToken
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $grantSql
        $command.CommandTimeout = 60
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
    Write-Host "Granted $AppIdentityName access to the coach database."
}
finally {
    $accessToken = $null
    if ($firewallRuleCreated) {
        Invoke-AzJson -Arguments @(
            'sql', 'server', 'firewall-rule', 'delete',
            '--resource-group', $ResourceGroup, '--server', $SqlServerName,
            '--name', $firewallRuleName
        ) | Out-Null
    }
}
