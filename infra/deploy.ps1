param(
    [Parameter(Mandatory = $true)][string]$AuthenticationAuthority,
    [Parameter(Mandatory = $true)][string]$Image,
    [Parameter(Mandatory = $true)][string]$RegistryUsername,
    [string]$ResourceGroup = 'rg-oakshore-coach',
    [string]$Location = 'northeurope',
    [string]$SqlLocation = 'westeurope',
    [string]$NamePrefix = 'oakshore-coach',
    [string]$SqlServerName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-AzJson([string[]]$Arguments) {
    $result = & az @Arguments --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: $($Arguments[0..([Math]::Min(2, $Arguments.Length - 1))] -join ' ')"
    }
    return $result | ConvertFrom-Json
}

if ($AuthenticationAuthority -notmatch '^https://[^/]+') {
    throw 'AuthenticationAuthority must be an HTTPS issuer URL.'
}
if ($Image -notmatch '^ghcr\.io/[^\s]+@sha256:[a-f0-9]{64}$' -and
    $Image -notmatch '^ghcr\.io/[^\s]+:[a-zA-Z0-9_.-]+$') {
    throw 'Image must be a GHCR image reference with an explicit tag or digest.'
}
if ($NamePrefix -notmatch '^[a-z][a-z0-9-]{2,24}$') {
    throw 'NamePrefix must be lowercase letters, digits, or hyphens, 3-25 characters.'
}
if ([string]::IsNullOrWhiteSpace($env:COACH_GHCR_TOKEN)) {
    throw 'Set COACH_GHCR_TOKEN to a GitHub token with read:packages access before deployment.'
}

$subscription = Invoke-AzJson -Arguments @('account', 'show')
if ([string]::IsNullOrWhiteSpace($SqlServerName)) {
    $SqlServerName = "${NamePrefix}-$($subscription.id.Substring(0, 8))-${SqlLocation}-sql"
}
$administrator = Invoke-AzJson -Arguments @('ad', 'signed-in-user', 'show')
if (-not $administrator.id -or -not $administrator.userPrincipalName) {
    throw 'The Azure account must have an Entra user object ID and user principal name.'
}

$foundationPath = Join-Path $PSScriptRoot 'foundation.bicep'
$appPath = Join-Path $PSScriptRoot 'app.bicep'
$identityName = "${NamePrefix}-app"

Write-Host "Deploying Coach resources in $ResourceGroup ($Location)"
Invoke-AzJson -Arguments @('group', 'create', '--name', $ResourceGroup, '--location', $Location) | Out-Null
$foundation = Invoke-AzJson -Arguments @(
    'deployment', 'group', 'create',
    '--name', 'coach-foundation',
    '--resource-group', $ResourceGroup,
    '--template-file', $foundationPath,
    '--parameters', "location=$Location", "sqlLocation=$SqlLocation", "namePrefix=$NamePrefix",
    "sqlServerName=$SqlServerName",
    "sqlAdministratorName=$($administrator.userPrincipalName)",
    "sqlAdministratorObjectId=$($administrator.id)"
)

$database = Invoke-AzJson -Arguments @('sql', 'db', 'show', '--resource-group', $ResourceGroup, '--server', $SqlServerName, '--name', 'coach')
if ($database.useFreeLimit -ne $true -or $database.freeLimitExhaustionBehavior -ne 'AutoPause') {
    throw 'Azure SQL did not enable the free offer with AutoPause. App deployment stopped.'
}
$entraOnly = Invoke-AzJson -Arguments @('sql', 'server', 'ad-only-auth', 'get', '--resource-group', $ResourceGroup, '--name', $SqlServerName)
if ($entraOnly.azureADOnlyAuthentication -ne $true) {
    throw 'SQL-only authentication remains enabled. App deployment stopped.'
}
$environment = Invoke-AzJson -Arguments @(
    'resource', 'show', '--resource-group', $ResourceGroup,
    '--resource-type', 'Microsoft.App/managedEnvironments',
    '--name', "${NamePrefix}-env"
)
if ($environment.properties.appLogsConfiguration.destination -notin @('', 'none') -or
    $environment.properties.appLogsConfiguration.logAnalyticsConfiguration) {
    throw 'Container Apps has a billed log destination. App deployment stopped.'
}

$sqlServerFqdn = $foundation.properties.outputs.sqlServerFqdn.value
& (Join-Path $PSScriptRoot 'grant-database-access.ps1') `
    -ResourceGroup $ResourceGroup -SqlServerName $SqlServerName `
    -SqlServerFqdn $sqlServerFqdn `
    -AppIdentityName $identityName

$app = Invoke-AzJson -Arguments @(
    'deployment', 'group', 'create',
    '--name', 'coach-app',
    '--resource-group', $ResourceGroup,
    '--template-file', $appPath,
    '--parameters', "location=$Location", "namePrefix=$NamePrefix",
    "sqlServerFqdn=$sqlServerFqdn", "image=$Image",
    "registryUsername=$RegistryUsername", "registryToken=$env:COACH_GHCR_TOKEN",
    "authenticationAuthority=$AuthenticationAuthority"
)
$appUrl = $app.properties.outputs.appUrl.value
Write-Host "Container App deployed: $appUrl"
Write-Host 'Verify /health, authentication, data persistence, and monthly cost before marking ticket 02 complete.'
