# Configuration

Local Agent Broker runs as an MCP stdio server. Pass one JSON-RPC message per input line. The server writes protocol responses to stdout and diagnostics to stderr.

## Command line

| Option | Action |
| --- | --- |
| `--repo-root <path>` | Select an existing workspace directory. Required unless `LOCAL_AGENT_BROKER_REPO_ROOT` or its legacy name is set. The workspace does not need `AGENTS.md`. |
| `--state-root <path>` | Select history, tray settings, and trace storage. The default is `<repo-root>/.local-agent-broker`. Exclude this directory from version control. |
| `--sessions-root <path>` | Enable named editor sessions from `session.json` files below this directory. Sessions are disabled by default. |
| `--integration xrengine` | Use the XRENGINE state and session paths when explicit roots are absent. |
| `--api-key-env <name>` | Name the OpenAI API key variable. The default is `OPENAI_API_KEY`. |
| `--anthropic-api-key-env <name>` | Name the Anthropic API key variable. The default is `ANTHROPIC_API_KEY`. |
| `--editor-auth-env <name>` | Name the optional editor authentication token variable. |

Command-line values take priority over environment values. For each setting, a `LOCAL_AGENT_BROKER_` variable takes priority over the matching legacy `XRE_LOCAL_AGENT_BROKER_` variable. The legacy names remain available for existing launch scripts.

| Variable suffix | Default | Limit or use |
| --- | --- | --- |
| `REPO_ROOT` | None | Existing workspace directory. |
| `STATE_ROOT` | `<repo-root>/.local-agent-broker` | Storage path. |
| `SESSIONS_ROOT` | Disabled | Session manifest directory. |
| `INTEGRATION` | None | Accepts `xrengine`. |
| `API_KEY_ENV` | `OPENAI_API_KEY` | Variable name, not a key value. |
| `ANTHROPIC_API_KEY_ENV` | `ANTHROPIC_API_KEY` | Variable name, not a key value. |
| `EDITOR_AUTH_ENV` | None | Variable name for an editor token. |
| `MAX_CONCURRENCY` | `4` | Global active-run limit, from 1 to 100. |
| `MAX_RUNS` | At least `32` or `MAX_CONCURRENCY` | Retained-run limit, from 4 to 256. It must be at least `MAX_CONCURRENCY`. |
| `RETENTION_MINUTES` | `120` | In-memory terminal-run retention, from 1 to 1440 minutes. |
| `TRACE` | `off` | Accepts `off` or `metadata`. The metadata trace contains no prompt text. |

For example, set `LOCAL_AGENT_BROKER_MAX_CONCURRENCY` to configure the global limit. The matching legacy name is `XRE_LOCAL_AGENT_BROKER_MAX_CONCURRENCY`. Do not put API key values in arguments or source files. The broker reads the selected key variable from its process environment. On Windows, it also checks the user environment when the process value is absent. `ANTHROPIC_WORKSPACE_ID` is optional for an Anthropic key with more than one workspace.

The XRENGINE integration resolves state under `<repo-root>/Build/_AgentValidation/00000000-000000-shared/local-agent-broker-ui` and sessions under `<repo-root>/Build/_AgentValidation/00000000-000000-shared/mcp-sessions`. Explicit `--state-root` and `--sessions-root` values override these defaults.

The Windows tray companion accepts `--repo-root <path>`, `--state-root <path>`, and `--show-history`. The broker passes its state root when it starts the tray. The tray uses the generic state default if it starts without `--state-root`.
