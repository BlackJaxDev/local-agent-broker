[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$BrokerPath,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$Workspace
)

$ErrorActionPreference = 'Stop'
$brokerPath = (Resolve-Path -LiteralPath $BrokerPath).Path
$workspacePath = (Resolve-Path -LiteralPath $Workspace).Path

$startInfo = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$startInfo.ArgumentList.Add($brokerPath)
$startInfo.ArgumentList.Add('--repo-root')
$startInfo.ArgumentList.Add($workspacePath)
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw 'Could not start the broker.'
}

function Send-McpMessage
{
    param([object]$Message)
    $process.StandardInput.WriteLine(($Message | ConvertTo-Json -Depth 20 -Compress))
    $process.StandardInput.Flush()
}

function Receive-McpResponse
{
    param([int]$Id)

    while ($true)
    {
        $line = $process.StandardOutput.ReadLineAsync().WaitAsync(
            [TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult()
        if ($null -eq $line) {
            throw "Broker closed stdout before response $Id."
        }

        $response = $line | ConvertFrom-Json -Depth 40
        if ($response.id -ne $Id) {
            continue
        }
        if ($null -ne $response.error) {
            throw "MCP response $Id failed: $($response.error.message)"
        }
        return $response.result
    }
}

try
{
    Send-McpMessage @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-03-26'
            capabilities = @{}
            clientInfo = @{ name = 'LocalAgentBroker.SmokeTest'; version = '1.0' }
        }
    }
    $initialization = Receive-McpResponse 1
    if ($initialization.serverInfo.name -ne 'LocalAgentBroker') {
        throw "Unexpected MCP server name: $($initialization.serverInfo.name)"
    }

    Send-McpMessage @{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
        params = @{}
    }
    Send-McpMessage @{
        jsonrpc = '2.0'
        id = 2
        method = 'tools/list'
        params = @{}
    }
    $toolList = Receive-McpResponse 2
    $expectedNames = @(
        'recommend_agent_route',
        'start_agent_run',
        'get_agent_run',
        'cancel_agent_run',
        'list_agent_runs'
    )
    $actualNames = @($toolList.tools | ForEach-Object { $_.name })
    if (@(Compare-Object $expectedNames $actualNames).Count -ne 0 -or
        $actualNames.Count -ne $expectedNames.Count)
    {
        throw "Unexpected MCP tools: $($actualNames -join ', ')"
    }

    Write-Host 'MCP initialize and tools/list passed.'
}
finally
{
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(2000)) {
        $process.Kill($true)
    }
    $process.Dispose()
}
