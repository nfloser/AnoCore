# Management API core

AnoCore exposes a transport-neutral management core before committing the CS2 plugin
to any HTTP server implementation.

## Versioning

The current management contract is `v1` through
`ManagementApiVersion.Current`. This version is independent from the module API
level. A future HTTP, CLI or panel adapter must identify the management version it
implements and translate requests into the contracts in `AnoCore.Abstractions`.

## Authentication

Management tokens are split into a bounded public token id and a secret. AnoCore's
runtime credential type stores only:

- token id;
- granted scopes;
- random salt;
- PBKDF2-SHA256 hash;
- iteration count.

The plaintext secret is not retained. Verification uses a constant-time hash
comparison. Credentials require a minimum 24-character printable secret, and duplicate
token ids are rejected.

A transport is responsible for obtaining the presented token id/secret securely. It
must not place secrets into logs, correlation ids, audit reasons or URLs.

## Authorization and capabilities

Management access is capability-based. A registered capability declares its required
scope and whether it is a read or privileged operation. The initial scopes are:

- `ReadStatus`
- `ManageServer`
- `ManagePlayers`
- `ManageModules`

Capabilities are explicitly registered by id. There is no arbitrary console-command
or reflection dispatch path.

Scope denial happens before rate limiting, auditing or handler execution. Handlers
receive a validated request context and a bounded argument dictionary.

## Rate limiting

The runtime limiter uses a one-minute fixed window keyed by authenticated token id and
operation class. Read and privileged limits are independent, and the number of tracked
keys is bounded to prevent unbounded memory growth.

This is a core safety limit, not a substitute for network-layer connection limits on a
future HTTP host.

## Audit semantics

An optional audit callback receives metadata only: phase, token id, capability id,
correlation id, result code and UTC timestamp. Request arguments and credentials are
not included.

If the requested audit fails, the capability is not executed. If execution has already
completed but the completion audit fails, the result is explicitly returned as
`audit_failed_after_execution` together with the original operation result code.
This prevents callers from assuming the action did not run.

Handler exceptions are converted to the public `handler_failed` code without exposing
exception text.

## Status surface

`IManagementStatusProvider` exposes:

- health/readiness backed by the shared database probe;
- safe server metadata (management API version, module API level, connected count);
- current connected player identity/name/team;
- module id and lifecycle state.

The provider deliberately excludes connection strings, host/IP details, exception
messages and configuration secrets.

## Hosting boundary

This package starts no network listener. The CounterStrikeSharp plugin therefore keeps
its current dependency footprint and does not acquire an ASP.NET/Kestrel dependency.

A later transport package may expose HTTPS or another management protocol by adapting
into these contracts. It must retain authentication, scope checks, rate limiting,
validation and audit behavior rather than bypassing the management core.
