# C0 probe: the orchestrator in a cloud VM

The orchestrator builds and runs in a Claude Code cloud VM. `claude -p` is signed in, and both plugin forms load the `orch` plugin. `doctor` fails on 3 checks: `gh`, the plugin settings file and the supervisor.

Date: 2026-11-17 (UTC). Source commit: `0e83e17` on `orch/session-c46c4a77`.

## Results

| Step | Command | Result | Time |
|---|---|---|---|
| 1 | `uname -a` | `Linux vm 6.18.44-fc-v553 x86_64` | <1 s |
| 1 | `nproc`, `free -h` | 4 CPUs, 15 GiB RAM, no swap | <1 s |
| 1 | `env` names | 61 `CLAUDE*`, 1 `ANTHROPIC*` (see below) | <1 s |
| 1 | `claude --version` | `2.1.326 (Claude Code)` | <1 s |
| 2 | `dotnet-install.sh --channel 10.0` | Exit 0, SDK `10.0.404` | 16 s |
| 3 | `dotnet build src/Orchestrator.Web` | Exit 0, 0 errors, 0 warnings | 11 s |
| 3 | `dotnet build src/Orchestrator.Cli` | Exit 0, 0 errors, 0 warnings | 2 s |
| 4 | `dotnet run` Web, `curl /api/sessions` | HTTP `200` | 3 s |
| 5 | `orch doctor` | Exit 1, 7 ok, 3 FAIL | 3 s |
| 6 | `claude -p --output-format json` | `success`, result `OK`, $0.32 | 5 s |
| 7 | `CLAUDE_CODE_PLUGIN_DIRS=… claude -p` | Lists the 6 `orch:` skills | 8 s |
| 7 | `claude -p --plugin-dir …` | Lists the 6 `orch:` skills | 8 s |
| 8 | `curl https://api.github.com` | `200` | 72 ms |
| 8 | `curl https://www.cloudflare.com` | `200` | 283 ms |
| 8 | `curl https://example.trycloudflare.com` | `000`, proxy error (see below) | 313 ms |
| 8 | `curl https://claude.ai` | `403` | 120 ms |

### Environment variable names

- `ANTHROPIC*`: `ANTHROPIC_BASE_URL`.
- `CLAUDE*` for the orchestrator: `CLAUDECODE`, `CLAUDE_CODE_REMOTE`, `CLAUDE_CODE_REMOTE_SESSION_ID`, `CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_CODE_CHILD_SESSION`, `CLAUDE_CODE_MESSAGING_SOCKET`, `CLAUDE_CODE_MESSAGING_TOKEN`, `CLAUDE_SESSION_INGRESS_TOKEN_FILE`.
- No `ANTHROPIC_API_KEY`. The CLI gets its credentials from the host (`CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST`).

### Step 5: doctor output

```text
ok   home: /home/user/playground/session-orchestrator
ok   install: built from 0e83e171, no newer change in the source
ok   app: not installed in /root/Apps/Orchestrator
ok   store: /tmp/orch-store
ok   policy: low 120 s, medium 30 s, high never
ok   templates: 10 kinds, each example is valid
ok   claude: 2.1.326 (Claude Code)
FAIL gh: not signed in: run gh auth login
FAIL plugin: Could not find file '/root/.claude/settings.json'.
FAIL supervisor: Connection refused (127.0.0.1:7777)
```

### Step 7 and step 8: exact errors

- Both step 7 runs print this line on stderr before the answer:
  `orch: hooks module not loaded: hooks modules are turned off for installed plugins in this process: this wake's flag fetch has not answered the rollout switch; they load at a turn start once it reads on; built-in plugins load regardless`
- Step 8, trycloudflare: `curl: (56) CONNECT tunnel failed, response 502`.

## Findings

- **Toolchain:** .NET 10 installs in 16 s without root. `Orchestrator.Web` and `Orchestrator.Cli` build without errors. The tray is not part of this probe.
- **Web server:** it starts in about 3 s with `ORCH_IMPORT=0` and answers `200` on `/api/sessions`.
- **`claude -p`:** it runs and is signed in through the host. One small call cost $0.32, possibly because of the large cached system prompt.
- **Plugin load:** both `CLAUDE_CODE_PLUGIN_DIRS` and `--plugin-dir` load the `orch` plugin. The plugin hooks module does not load in a child `claude -p` process (rollout switch).
- **This session:** it does not list an `orch:` skill. The plugin is not installed for the cloud session.
- **doctor in the cloud:** `gh` is not signed in, `/root/.claude/settings.json` does not exist, and no supervisor listens on port 7777. The cloud session uses the GitHub MCP tools, not `gh`.
- **Network:** the proxy allows GitHub and Cloudflare. It refuses the `trycloudflare.com` tunnel host with 502, so a quick tunnel from the VM is not reachable this way. `claude.ai` returns 403 to a plain `curl`.
