# Warning commands (draft acceptance)

The plugin registers these commands after the MariaDB-backed runtime is ready:

| Command | Permission | Behavior |
| --- | --- | --- |
| `css_anowarn <online-target> <minutes> [reason]` | `ano.admin.warn` | Store a warning; zero minutes means permanent. |
| `css_anoclearwarns <online-target> [reason]` | `ano.admin.clearwarns` | Clear all currently active warnings while retaining history. |
| `css_anowarns <target> ` | `ano.admin.warns` | Read the ten newest historical warnings of an authorized target. |
| `css_anomywarns` | none | A connected player reads their own ten newest warning records. |

Use a quoted single argument for a reason containing spaces. Omitted reasons use `No reason provided.`. The existing target gateway applies actor permission, immunity, self-target and target-session checks. Mutations require an online player; administrative history may also use an explicit offline SteamID64. The server console may execute administrative commands; it cannot read personal warning history.

Mutation audit records a requested action before storage, then a completed action after storage. A requested record without a matching completion requires investigation; database writes and audit entries cannot be one atomic operation. The warning table itself retains actor, reason, expiry, clear actor and clear reason. Query results are bounded to ten records.

## CS2 acceptance before marking complete

On a disposable server with MariaDB and two clients, verify each permission, immunity and self-target denial; quoted reasons and duration expiry; history after reconnect/restart; clearing and retained history; disconnect/reconnect during an audit delay; chat/console output; and unload/reload. Confirm direct player notification and record the exact plugin SHA, host/runtime versions and observations. Until these checks pass, PR #73 remains draft.
