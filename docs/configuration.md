# Configuration

Local Agent Broker runs as an MCP stdio server. Send one JSON-RPC message per input line. The server writes protocol responses to stdout and diagnostics to stderr.

Select the workspace explicitly. Set `--repo-root`, `LOCAL_AGENT_BROKER_REPO_ROOT`, or `repositoryRoot` in a selected config file. The directory must exist. The broker does not discover a workspace or require an `AGENTS.md` file.

## Sources and precedence

For each setting, the broker uses the first available value in this order: command line, `LOCAL_AGENT_BROKER_*` environment variable, selected JSON config file, default. Select the config file with `--config <path>` or `LOCAL_AGENT_BROKER_CONFIG`. There is no automatic config file search. A relative config file path starts at the process working directory.

A relative `repositoryRoot` in the JSON file starts at the config file directory. A relative `--repo-root` or `LOCAL_AGENT_BROKER_REPO_ROOT` starts at the process working directory. Relative state and sessions roots start at the final workspace root, regardless of their source.

| Command line | Environment suffix | JSON field | Default and limit |
| --- | --- | --- | --- |
| `--config <path>` | `CONFIG` | — | No config file. |
| `--repo-root <path>` | `REPO_ROOT` | `repositoryRoot` | Required existing directory. |
| `--state-root <path>` | `STATE_ROOT` | `stateRoot` | `<repo-root>/.local-agent-broker`. Exclude it from version control. |
| `--sessions-root <path>` | `SESSIONS_ROOT` | `sessionsRoot` | None. Named editor sessions are disabled. |
| `--api-key-env <name>` | `API_KEY_ENV` | `apiKeyEnvironmentVariable` | `OPENAI_API_KEY`. |
| `--anthropic-api-key-env <name>` | `ANTHROPIC_API_KEY_ENV` | `anthropicApiKeyEnvironmentVariable` | `ANTHROPIC_API_KEY`. |
| `--editor-auth-env <name>` | `EDITOR_AUTH_ENV` | `editorAuthTokenEnvironmentVariable` | None. |
| `--max-concurrency <number>` | `MAX_CONCURRENCY` | `maximumConcurrentRuns` | 4; range 1–100. |
| `--max-runs <number>` | `MAX_RUNS` | `maximumRetainedRuns` | At least 32 or the concurrency limit; range 4–256. Must be at least the concurrency limit. |
| `--retention-minutes <number>` | `RETENTION_MINUTES` | `retentionMinutes` | 120; range 1–1440. |
| `--trace <mode>` | `TRACE` | `trace` | `off`; also accepts `metadata`. The metadata trace has no prompt text. |
| `--launch-tray true\|false` | `LAUNCH_TRAY` | `launchTray` | `true`. Set `false` for CI or headless use. |
| — | — | `repositoryAccess` | Empty text policy additions. See below. |

Use a provider key variable name in `apiKeyEnvironmentVariable` or `anthropicApiKeyEnvironmentVariable`. Do not put a key value in the config file, arguments, or source files. The broker reads the selected variable from its process environment. On Windows, it also checks the user environment when the process value is absent. `ANTHROPIC_WORKSPACE_ID` is optional for an Anthropic key with more than one workspace.

The JSON file uses camelCase names. Unknown fields, including unknown fields in `repositoryAccess`, cause a configuration error. This check helps find typing errors.

```json
{
  "repositoryRoot": ".",
  "stateRoot": ".local-agent-broker",
  "sessionsRoot": ".broker-sessions",
  "maximumConcurrentRuns": 4,
  "launchTray": false,
  "repositoryAccess": {
    "additionalTextExtensions": [".wgsl"],
    "additionalTextFileNames": ["Containerfile"],
    "excludedPaths": ["private"]
  }
}
```

`repositoryAccess` accepts `additionalTextExtensions`, `additionalTextFileNames`, and `excludedPaths`, each a list of strings. These additions do not override blocked secret files, traversal checks, reparse-point checks, the state root exclusion, or the sessions root exclusion. Run requests still need explicit allowed repository roots before they can read files.

## Editor sessions and tray

Each named editor session uses a `session.json` manifest below `sessionsRoot`. The manifest has `name` and `endpoint` fields. The endpoint must be loopback HTTP or HTTPS. A run names the session with the existing `editor_session` request field. A configured sessions root does not grant editor tools to a run. With an empty `sessionIdentityPath`, the preflight checks endpoint liveness only. Set a path such as `["session", "name"]` to require that ping result property to equal the requested session name. Missing, non-string, or unequal identities fail the run. The broker does not start or discover editor processes.

The Windows tray companion accepts `--repo-root <path>`, `--state-root <path>`, and `--show-history`. The broker passes its state root when it starts the tray. Set `launchTray` to `false` when the tray is not needed.

The build wrapper uses the `LOCAL_AGENT_BROKER_BUILD_MUTEX` environment variable to select a Windows build lock. The default is `Local\LocalAgentBroker-Build`. This build setting is independent of the broker process configuration.
