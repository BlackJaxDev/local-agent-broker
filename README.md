# Local Agent Broker

Local Agent Broker is a local MCP stdio server. It starts bounded OpenAI Responses or Anthropic Messages API workers. A worker is a separate, billed API request. It does not change the model of the host application.

The server provides five tools: `recommend_agent_route`, `start_agent_run`, `get_agent_run`, `cancel_agent_run`, and `list_agent_runs`. A run uses the exact requested model. The server reports a model mismatch as a failure.

## Requirements

- .NET 10 SDK for source builds.
- .NET 10 runtime for framework-dependent output.
- PowerShell 7 for the scripts in `Tools/`.
- An API key in `OPENAI_API_KEY` or `ANTHROPIC_API_KEY` for paid runs. The smoke check and unit tests do not need a key.
- An existing workspace directory. An `AGENTS.md` file is optional.

The tray companion uses Windows. The MCP server can run without it.

See [configuration](docs/configuration.md) for all settings and [the protocol guide](docs/protocol.md) for run states, tools, and access controls.

## Build and test

```powershell
& ./Tools/Invoke-Build.ps1 -DotnetArguments @('restore', 'LocalAgentBroker.slnx')
& ./Tools/Invoke-Build.ps1 -DotnetArguments @('build', 'LocalAgentBroker.slnx', '--no-restore')
& ./Tools/Invoke-Build.ps1 -DotnetArguments @('test', 'tests/LocalAgentBroker.Tests/LocalAgentBroker.Tests.csproj', '--no-restore')
```

The build wrapper limits MSBuild workers. Set `LOCAL_AGENT_BROKER_BUILD_MUTEX` to share its Windows build lock with another build process. Its default is `Local\LocalAgentBroker-Build`.

## Run

Build the server, then configure an MCP client to launch the DLL with an explicit workspace path:

```powershell
dotnet ./src/LocalAgentBroker/bin/Debug/net10.0/LocalAgentBroker.dll --repo-root <workspace>
```

The server reads newline-delimited JSON-RPC from stdin and writes MCP responses to stdout. The default state root is `.local-agent-broker/` in the workspace; exclude it from version control. Use `--config <path>` or `LOCAL_AGENT_BROKER_CONFIG` to select a JSON configuration file. A workspace path is required in the CLI, environment, or file. The broker does not discover or trust a workspace automatically. See [configuration](docs/configuration.md) for path rules and all settings.

For a local protocol check with no API request:

```powershell
& ./Tools/Test-Mcp.ps1 -BrokerPath ./src/LocalAgentBroker/bin/Debug/net10.0/LocalAgentBroker.dll -Workspace <workspace>
```

The check validates the server name and all five tool names. It does not call `start_agent_run`.

## Publish

```powershell
& ./Tools/Publish.ps1
& ./Tools/Publish.ps1 -IncludeTray
```

The script publishes the server to `artifacts/broker/` and packs `LocalAgentBroker.Orchestration` to `artifacts/packages/`. `-IncludeTray` publishes the Windows tray companion to `artifacts/broker/tray/`, where the server can find it. Release archives, packages, and `SHA256SUMS.txt` are written to `artifacts/release/`. `-SkipPackage` omits the package.

## Access boundaries

A run has turn, tool, byte, retry, and concurrency budgets. Repository access and editor access are opt-in. Read-only repository tools require explicit allowed roots. A configured sessions root enables named session manifests; it does not grant editor access to a run. Editor mutation requires an explicit allowlist and a named session that permits mutation. Destructive editor access needs separate authority. Configure provider key variable names, never key values, in broker settings. Do not pass keys in tool arguments, logs, or source files.

The caller is responsible for the cost of each provider request. Check `requested_model` and `actual_model` in the run result before using its evidence. Review any proposed code changes and validate them in the local workspace.

## License

This project uses the [MIT License](LICENSE.md). Third-party components retain their own licenses. Their notices remain in [docs/licenses/](docs/licenses/) and the tray vendor directory.
