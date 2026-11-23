#!/usr/bin/env bash
# The setup script of the cloud environment of an orch cloud worker (G-4).
# The cloud environment runs it in a new VM before the session starts.
# Recipe: docs/know-how.md, section "Cloud environment". Variables: environment.env.
# It installs .NET 10 and the orch command, and writes the permissions of
# Claude Code in the VM (C0-3). It reads no secret and writes no secret.
set -euo pipefail

HOME_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
DOTNET_DIR="${DOTNET_ROOT:-$HOME/.dotnet}"
TOOLS_DIR="$HOME/.dotnet/tools"
PACK_DIR="$(mktemp -d)"

say() { printf 'orch setup: %s\n' "$*"; }

# 1. .NET 10 SDK, without root (C0: 16 s).
if ! "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  say "install .NET 10 in $DOTNET_DIR"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$PACK_DIR/dotnet-install.sh"
  bash "$PACK_DIR/dotnet-install.sh" --channel 10.0 --install-dir "$DOTNET_DIR" >/dev/null
fi
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$TOOLS_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# 2. The orch command from this checkout (C5-1: the plugin and the worker start orch from PATH).
say "pack and install orch from $HOME_DIR"
dotnet pack "$HOME_DIR/src/Orchestrator.Cli" -c Release -o "$PACK_DIR" >/dev/null
dotnet tool update -g Orchestrator.Cli --add-source "$PACK_DIR" >/dev/null

# 3. orch and dotnet on the PATH of each process, also a process that does not read a profile.
BIN_DIR=/usr/local/bin
if [ ! -w "$BIN_DIR" ]; then BIN_DIR="$HOME/.local/bin"; mkdir -p "$BIN_DIR"; fi
ln -sf "$DOTNET_DIR/dotnet" "$BIN_DIR/dotnet"
cat > "$BIN_DIR/orch" <<WRAP
#!/usr/bin/env bash
export DOTNET_ROOT="$DOTNET_DIR"
exec "$TOOLS_DIR/orch" "\$@"
WRAP
chmod +x "$BIN_DIR/orch"
for profile in "$HOME/.bashrc" "$HOME/.profile"; do
  grep -q 'orch cloud worker' "$profile" 2>/dev/null || cat >> "$profile" <<PROFILE
# orch cloud worker
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$BIN_DIR:$DOTNET_DIR:$TOOLS_DIR:\$PATH"
PROFILE
done

# 4. The permissions of Claude Code in this VM (C0-3): the auto mode of the supervisor turns, and orch always allowed.
# The file is in the VM only. An existing file stays as it is.
SETTINGS="$HOME/.claude/settings.json"
if [ ! -f "$SETTINGS" ]; then
  mkdir -p "$HOME/.claude"
  cat > "$SETTINGS" <<'JSON'
{
  "permissions": {
    "defaultMode": "auto",
    "allow": ["Bash(orch:*)", "Bash(dotnet:*)", "Bash(git:*)", "Bash(node:*)"]
  }
}
JSON
  say "wrote $SETTINGS"
else
  say "$SETTINGS exists: add the allow list of environment.env by hand if a prompt stops the worker"
fi

# 5. Checks. ORCH_URL comes from the variables of the cloud environment.
"$BIN_DIR/orch" --help >/dev/null
if [ -z "${ORCH_URL:-}" ]; then
  say "ORCH_URL is not set: orch works on a local store in this VM, not on the supervisor"
else
  say "ORCH_URL is $ORCH_URL"
fi
rm -rf "$PACK_DIR"
say "done: orch $(command -v orch || echo "$BIN_DIR/orch"), .NET $(dotnet --version)"
