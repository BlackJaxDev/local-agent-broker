[CmdletBinding()]
param(
    [switch]$IncludeTray,
    [switch]$SkipPackage
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$buildScript = Join-Path $PSScriptRoot 'Invoke-Build.ps1'
$brokerProject = Join-Path $repoRoot 'src/LocalAgentBroker/LocalAgentBroker.csproj'
$trayProject = Join-Path $repoRoot 'src/LocalAgentBroker.Tray/LocalAgentBroker.Tray.csproj'
$orchestrationProject = Join-Path $repoRoot 'src/LocalAgentBroker.Orchestration/LocalAgentBroker.Orchestration.csproj'

function Invoke-BrokerBuild
{
    param([string[]]$Arguments)

    & $buildScript -DotnetArguments $Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Build command failed with exit code $LASTEXITCODE."
    }
}

Invoke-BrokerBuild @(
    'publish', $brokerProject, '--configuration', 'Release',
    '--output', (Join-Path $artifactsRoot 'broker')
)

if ($IncludeTray)
{
    if (-not $IsWindows) {
        throw 'The tray companion requires Windows.'
    }

    Invoke-BrokerBuild @(
        'publish', $trayProject, '--configuration', 'Release',
        '--runtime', 'win-x64', '--self-contained', 'false',
        '--output', (Join-Path $artifactsRoot 'broker/tray')
    )
}

if (-not $SkipPackage)
{
    $packagesRoot = Join-Path $artifactsRoot 'packages'
    New-Item -ItemType Directory -Force -Path $packagesRoot | Out-Null
    Invoke-BrokerBuild @(
        'msbuild', $orchestrationProject, '-target:Pack',
        '-property:Configuration=Release',
        "-property:PackageOutputPath=$packagesRoot"
    )
}

& (Join-Path $PSScriptRoot 'Package-Release.ps1') -IncludeTray:$IncludeTray -SkipPackage:$SkipPackage
if ($LASTEXITCODE -ne 0)
{
    throw "Release packaging failed with exit code $LASTEXITCODE."
}

Write-Host "Published artifacts to $artifactsRoot"
