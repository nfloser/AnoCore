# Protected server controls

Issue #168 adds a deliberately narrow administration surface for server controls that should not be exposed as arbitrary console execution.

## Commands and permissions

| Command | Permission | Behavior |
| --- | --- | --- |
| `anocvar <name> <value>` | `ano.admin.cvar` | sets an explicitly allow-listed ConVar |
| `anoserver <command> [arguments]` | `ano.admin.server` | executes an explicitly allow-listed server command |
| `anosameip` / `anoantighosting` | `ano.admin.sameip` | lists connected players sharing one normalized network address without showing the address |

Server-console callers are allowed, while player callers still pass the normal command-registry permission check and a second permission check in the executor.

## Allow-list configuration

Protected CVar and server-command access is disabled by default. Add only the exact controls that should be available to `config/core.json`:

```json
{
  "ConnectionString": "",
  "ProtectedServerControls": {
    "AllowedConVars": [
      "mp_friendlyfire"
    ],
    "AllowedServerCommands": [
      "mp_restartgame"
    ]
  }
}
```

Names are normalized to lower case and must start with a letter. The built-in deny list still blocks password, token, Steam account and other credential-like ConVars even if they are accidentally configured. High-risk server commands such as `exec`, `map`, `changelevel`, `quit`, `rcon` and plugin-management commands are also denied regardless of configuration.

Invalid protected-control configuration fails closed: AnoCore keeps running, but CVar and server-command allow-lists are treated as empty until the configuration is corrected and the plugin is restarted.

## Input and audit safety

Values and command arguments are bounded to 256 characters and reject control characters, semicolons and backticks before any engine call. The command name and ConVar name must pass both syntax validation and the explicit allow-list.

Administrative audit records contain the normalized control name but redact values and server-command arguments. The live CounterStrikeSharp adapter performs the final ConVar write or server command on the server world-update thread.

## Same-network inspection

The live adapter reads addresses only while building the current connected-player grouping. Raw addresses are never returned by the module contract and are never included in the command response or audit record.

IPv4-mapped IPv6 addresses are normalized before grouping. A per-process random HMAC key converts the normalized address to a short `network-...` fingerprint, so the output can correlate players in the same group without exposing a reusable raw-IP-derived identifier. Only groups with at least two currently tracked human players are returned. Module output is additionally bounded by group count, player count and total message length.

## Native acceptance

On a disposable CS2 server:

1. Configure one harmless test ConVar and one harmless server command in the allow-list.
2. Verify authorized admins can use both commands and unauthorized players cannot.
3. Verify an unlisted control, a built-in denied control and an argument containing a separator are rejected before any server-side effect.
4. Confirm the administrative audit contains the action/control name but not the supplied value or arguments.
5. Connect two test clients from the same external address and one from a different address. Confirm `anosameip` groups only the matching pair, shows no raw IP/port and remains bounded with many clients.
6. Restart the plugin/server and confirm the fingerprint may change while grouping behavior remains correct.

Native behavior is still a release gate; automated tests cover the policy, permission, redaction, command-registration rollback and bounded module output but do not substitute for a real CounterStrikeSharp server run.
