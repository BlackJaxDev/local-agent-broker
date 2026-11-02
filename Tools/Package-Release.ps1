[CmdletBinding()]
param(
    [switch]$IncludeTray,
    [switch]$SkipPackage
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$brokerRoot = Join-Path $artifactsRoot 'broker'
$releaseRoot = Join-Path $artifactsRoot 'release'

[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$buildProperties.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version))
{
    throw 'Directory.Build.props must define Version.'
}
if (-not (Test-Path -LiteralPath (Join-Path $brokerRoot 'LocalAgentBroker.dll') -PathType Leaf))
{
    throw 'Publish the broker before packaging a release.'
}

$files = [System.Collections.Generic.SortedDictionary[string, string]]::new(
    [System.StringComparer]::Ordinal)
$files.Add('README.md', (Join-Path $repoRoot 'README.md'))
$files.Add('LICENSE.md', (Join-Path $repoRoot 'LICENSE.md'))

foreach ($directory in @('docs'))
{
    $directoryPath = Join-Path $repoRoot $directory
    if (-not (Test-Path -LiteralPath $directoryPath -PathType Container))
    {
        continue
    }
    foreach ($path in [System.IO.Directory]::GetFiles($directoryPath, '*', [System.IO.SearchOption]::AllDirectories))
    {
        $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $path).Replace('\', '/')
        $files.Add($relativePath, $path)
    }
}

foreach ($path in [System.IO.Directory]::GetFiles($brokerRoot, '*', [System.IO.SearchOption]::AllDirectories))
{
    $relativePath = [System.IO.Path]::GetRelativePath($brokerRoot, $path).Replace('\', '/')
    if ($relativePath -in @('README.md', 'LICENSE.md') -or
        $relativePath.StartsWith('licenses/', [System.StringComparison]::Ordinal))
    {
        continue
    }
    if (-not $IncludeTray -and $relativePath.StartsWith('tray/', [System.StringComparison]::Ordinal))
    {
        continue
    }
    $files.Add($relativePath, $path)
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
$archiveName = if ($IncludeTray)
{
    "local-agent-broker-$version-win-x64.zip"
}
else
{
    "local-agent-broker-$version.zip"
}
$otherArchiveName = if ($IncludeTray)
{
    "local-agent-broker-$version.zip"
}
else
{
    "local-agent-broker-$version-win-x64.zip"
}
$otherArchivePath = Join-Path $releaseRoot $otherArchiveName
if (Test-Path -LiteralPath $otherArchivePath -PathType Leaf)
{
    Remove-Item -LiteralPath $otherArchivePath
}
$archivePath = Join-Path $releaseRoot $archiveName
$archiveStream = [System.IO.File]::Open(
    $archivePath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
try
{
    $archive = [System.IO.Compression.ZipArchive]::new(
        $archiveStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try
    {
        $fixedTime = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        foreach ($file in $files.GetEnumerator())
        {
            $entry = $archive.CreateEntry(
                $file.Key, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTime
            $inputStream = [System.IO.File]::OpenRead($file.Value)
            try
            {
                $outputStream = $entry.Open()
                try
                {
                    $inputStream.CopyTo($outputStream)
                }
                finally
                {
                    $outputStream.Dispose()
                }
            }
            finally
            {
                $inputStream.Dispose()
            }
        }
    }
    finally
    {
        $archive.Dispose()
    }
}
finally
{
    $archiveStream.Dispose()
}

$releaseFiles = [System.Collections.Generic.List[string]]::new()
$releaseFiles.Add($archivePath)
$packageName = "LocalAgentBroker.Orchestration.$version.nupkg"
$releasePackagePath = Join-Path $releaseRoot $packageName
if (-not $SkipPackage)
{
    $packagePath = Join-Path $artifactsRoot "packages/$packageName"
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf))
    {
        throw "Package is missing: $packageName"
    }
    Copy-Item -LiteralPath $packagePath -Destination $releasePackagePath -Force
    $releaseFiles.Add($releasePackagePath)
}
elseif (Test-Path -LiteralPath $releasePackagePath -PathType Leaf)
{
    Remove-Item -LiteralPath $releasePackagePath
}

$hashLines = foreach ($path in $releaseFiles)
{
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($path))"
}
[System.IO.File]::WriteAllText(
    (Join-Path $releaseRoot 'SHA256SUMS.txt'),
    (($hashLines -join "`n") + "`n"),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Release package: $archivePath"
