# MCP protocol and run policy

The server exposes five MCP tools. `initialize` reports server name `LocalAgentBroker` and version `0.2.0`. `tools/list` returns the current input schemas. Use those schemas and the live `AgentModelCatalog` for exact model IDs and limits. Catalog admission does not prove provider access to a model.

| Tool | Action |
| --- | --- |
| `recommend_agent_route` | Suggest an exact model for an objective and constraints. It does not start a run. |
| `start_agent_run` | Validate a bounded request, queue a separate provider API run, and return its run ID. |
| `get_agent_run` | Return status, incremental output, usage, provider diagnostics, evidence, and any terminal result. |
| `cancel_agent_run` | Request cancellation of a queued or running run. |
| `list_agent_runs` | Return recent run metadata. `limit` defaults to 20 and is bounded to 1 through 100. |

Call `start_agent_run` with `objective` and an exact `requested_model`. Add success criteria, constraints, and a small evidence packet. The default budget allows 3 turns, 8 local tool calls, 1 retry, and 1 per-run concurrent operation. It limits each tool result to 262144 bytes. The default context limits are 16 files, 262144 raw bytes per file, 1048576 aggregate raw bytes, and 2097152 rendered bytes. The default output-token and elapsed-time broker limits are disabled. Provider limits still apply. Set positive limits when needed.

`start_agent_run` returns `queued` with a run ID. Poll `get_agent_run` until the status is `completed`, `failed`, or `cancelled`. `queued` and `running` are nonterminal. Inspect both `requested_model` and `actual_model`; the broker treats an exact-model mismatch as a failure. A failure can include provider-attempt diagnostics. A cancellation request is not a terminal result; poll until the status changes.

## Local access

Without `repository_access` or `editor_session`, a worker gets no local tools. `context_files` attaches immutable, repository-relative UTF-8 text snapshots at admission. The files go to the selected provider. An optional complete-file SHA-256 rejects changed input. The broker rejects path traversal, reparse-point escape, generated output, common secret containers, and the configured state root.

Set `repository_access.enabled` and at least one explicit `allowed_roots` directory to expose the read-only `repository_search` and `repository_read_text` tools. A positive tool-call budget is required. Use `.` only when the entire eligible source tree is authorized. Repository tools do not write files. Their results go to the selected provider.

An `editor_session` must name one manifest in the configured sessions root. The broker accepts only loopback HTTP or HTTPS endpoints. Editor access is read-only by default. `tool_policy.denied_tools` removes tools; a nonempty `allowed_tools` list limits tools to exact names. To permit mutation, set `allow_mutation` and provide a nonempty allowlist. To permit a tool marked destructive, also set `allow_destructive`. The named editor session must grant that operation. Read back changes and verify the result. The broker does not discover or start editor processes.

## Code swarms

The optional `swarm` request uses exact `allowed_paths` and immutable source snapshots. The current catalog accepts `gpt-6-luna` at `max` effort or `claude-haiku-5-5` at a supported effort. A swarm cannot use editor, repository, or hosted tools. It returns reviewed code proposals by default. `swarm.auto_apply` permits host writes only to the allowed paths and currently requires Windows handle validation. Use it only with authority to edit those files. Inspect the change list, review chain, failures, and applied paths. Validate the result in the local workspace.

Every provider run is billed separately. The caller sets the authorization and cost boundary. The broker does not switch the host application's model.

The HTTP tool client supports stateless JSON responses to `ping`, `tools/list`, and `tools/call`. It does not implement SSE or Streamable HTTP session negotiation. Tool annotations control access; a tool name does not grant read-only permission. Missing annotations make a tool potentially mutating and destructive.
