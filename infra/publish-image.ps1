param(
    [Parameter(Mandatory = $true)][string]$GitHubUsername,
    [string]$PackageName = 'oakshore-coach-api',
    [string]$Tag = (Get-Date -Format 'yyyyMMddHHmmss')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($env:COACH_GHCR_TOKEN)) {
    throw 'Set COACH_GHCR_TOKEN to a GitHub token with write:packages and read:packages access.'
}
if ($GitHubUsername -notmatch '^[A-Za-z0-9-]+$' -or
    $PackageName -notmatch '^[a-z0-9-]+$' -or
    $Tag -notmatch '^[A-Za-z0-9_.-]+$') {
    throw 'GitHubUsername, PackageName, or Tag has invalid characters.'
}

$image = "ghcr.io/$($GitHubUsername.ToLowerInvariant())/${PackageName}:$Tag"
$root = Split-Path -Parent $PSScriptRoot
$dockerConfig = Join-Path $root ('.local/docker-publish-' + [guid]::NewGuid().ToString('N'))
$previousDockerConfig = $env:DOCKER_CONFIG
try {
    New-Item -ItemType Directory -Path $dockerConfig -Force | Out-Null
    $env:DOCKER_CONFIG = $dockerConfig
    $auth = [Convert]::ToBase64String(
        [Text.Encoding]::ASCII.GetBytes("${GitHubUsername}:$env:COACH_GHCR_TOKEN"))
    @{ auths = @{ 'ghcr.io' = @{ auth = $auth } } } |
        ConvertTo-Json -Depth 5 -Compress |
        Set-Content -LiteralPath (Join-Path $dockerConfig 'config.json') -Encoding ASCII -NoNewline
    $auth = $null

    docker build --platform linux/amd64 --tag $image $root
    if ($LASTEXITCODE -ne 0) { throw 'Container image build failed.' }
    docker push $image
    if ($LASTEXITCODE -ne 0) { throw 'GHCR image publication failed.' }

    $digest = docker image inspect $image --format '{{index .RepoDigests 0}}'
    if ($LASTEXITCODE -ne 0 -or $digest -notmatch '^ghcr\.io/.+@sha256:[a-f0-9]{64}$') {
        throw 'Could not read the published image digest.'
    }
    Write-Host "Published $digest"
}
finally {
    $env:DOCKER_CONFIG = $previousDockerConfig
    $localRoot = [IO.Path]::GetFullPath((Join-Path $root '.local'))
    $resolvedConfig = [IO.Path]::GetFullPath($dockerConfig)
    if (-not $resolvedConfig.StartsWith(
        $localRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The temporary Docker configuration is outside the repository local directory.'
    }
    if ([IO.Directory]::Exists($resolvedConfig)) {
        [IO.Directory]::Delete($resolvedConfig, $true)
    }
}
