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


## HTTP wire adapter

`ManagementHttpAdapter` defines the bounded HTTP-facing v1 contract without starting
a listener or adding ASP.NET/Kestrel to the CounterStrikeSharp plugin.

Supported routes are:

- `GET /api/v1/status/health`
- `GET /api/v1/status/server`
- `GET /api/v1/status/players`
- `GET /api/v1/status/modules`
- `POST /api/v1/operations/{capability-id}`

Every request must provide:

- `X-AnoCore-Token`: the bounded public token id;
- `X-Correlation-ID`: a caller-generated bounded correlation id;
- `Authorization: Bearer <secret>`: the token secret.

Operation request bodies are a single JSON object whose string properties become the
bounded management argument dictionary. Nested objects, arrays and non-string values
are rejected. Bodies are capped before JSON parsing, as are paths and headers.

The adapter maps the management result to conservative HTTP status codes and always
returns JSON with `Cache-Control: no-store` and
`X-Content-Type-Options: nosniff`. Credentials are never copied into response
payloads.

### Hosting requirements

A network host is intentionally separate from the CS2 plugin. A production host that
wraps this adapter must:

1. terminate TLS before accepting management credentials;
2. expose only the documented routes and methods;
3. preserve the adapter's body/header limits and cancellation;
4. pass only a bounded, non-secret remote identity such as a trusted proxy identity;
5. add network-layer connection/request limits in addition to AnoCore's per-token
   management limiter;
6. keep bearer secrets out of access logs, tracing and URLs;
7. reject untrusted forwarded headers unless they come from an explicitly configured
   reverse proxy.

This boundary lets a Kestrel, reverse-proxy or other HTTPS host reuse the same
authentication, authorization, audit and capability gateway instead of duplicating
security policy.

## Local named-pipe bridge

The CounterStrikeSharp plugin can opt into a local named-pipe bridge without hosting
HTTP or Kestrel in the game-server process. The bridge is disabled by default and is
configured independently from the database settings in
`plugins/AnoCore/config/management.json`.

The default file is:

```json
{
  "Enabled": false,
  "PipeName": "anocore-management",
  "ReadRequestsPerMinute": 120,
  "PrivilegedRequestsPerMinute": 30,
  "MaximumTrackedKeys": 1024,
  "Credentials": []
}
```

When `Enabled` is `true`, at least one credential is required. Credential entries
contain only the public token id, granted scopes, PBKDF2 salt/hash material and
iteration count:

```json
{
  "TokenId": "panel",
  "Scopes": ["ReadStatus"],
  "SaltBase64": "<generated salt>",
  "HashBase64": "<generated hash>",
  "Iterations": 210000
}
```

Do not put a bearer secret into `management.json`. Provision credentials with
`ManagementTokenHasher.Create` and serialize the result through
`ManagementCredentialConfiguration.FromCredential`; retain the plaintext secret only
in the external caller's secret store.

The plugin builds `ManagementTokenAuthenticator` and `ManagementApiGateway` from
the live runtime services, so pipe traffic uses the same status provider, capability
registry and per-token rate limiter as the management core. Invalid management
configuration fails startup rather than silently enabling a weaker bridge.

### Pipe protocol and lifecycle

Each local connection carries exactly one request and one response. Frames use a
4-byte little-endian length prefix followed by UTF-8 JSON and are capped at 128 KiB.
The payload is the existing `ManagementHttpRequest`/`ManagementHttpResponse`
contract, including the token id, correlation id and bearer credential headers.

The server pipe is created with `CurrentUserOnly` and accepts one connection at a
time. That is a local process boundary, not a network security boundary. Any future
sidecar that exposes the API off-host still has to terminate TLS and follow the HTTP
hosting requirements above.

The bridge is created during asynchronous runtime composition but starts only after
AnoCore has successfully activated its runtime. Unload and hot reload stop the pipe
before disposing runtime services. A pipe creation failure rolls activation back, and
malformed client frames are rejected without terminating the next listener cycle.

`ManagementPipeClient` separately bounds connection establishment (5 seconds by
default) and the entire write/read exchange after connecting (30 seconds by
default). Both deadlines can be configured up to one minute. A silent connected
peer therefore cannot keep a caller waiting indefinitely. Caller cancellation is
preserved as cancellation; an internal deadline produces `TimeoutException`.
A response timeout or cancellation after dispatch does not prove a mutation did
not execute. Do not automatically retry privileged operations after either result.
