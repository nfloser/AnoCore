# Optional moderation webhooks

`config/moderation-webhooks.json` defaults to disabled. It contains `Enabled`,
`AllowedHosts`, `PollSeconds` (1–60), `TimeoutSeconds` (1–10), `BatchSize` (1–64),
`IncludeTargetIds` and `IncludeReasons` (both false). Enable only after explicitly
approving the destination and the fields to leave the server. Settings are
restart-only. Supply the URL through `ANOCORE_MODERATION_WEBHOOK_URL`; an optional
Bearer credential comes from `ANOCORE_MODERATION_WEBHOOK_TOKEN`. Neither secret is
written into config or logged. No URL/token is included in error output.

One background worker reads committed shared administration and moderation audits
and sends a small JSON `content` message. Discord-style `allowed_mentions` is empty;
player IDs/reasons are excluded unless explicitly enabled. No IP/connection data
is sent. Sanction/role mutations and native gameplay do not await HTTP delivery.

Transport requires HTTPS port 443 and an exactly approved DNS host. Redirects,
cookies and proxies are disabled. Each connection resolves DNS, rejects non-public
addresses and connects directly to the validated address, retaining normal TLS
host/certificate validation. This prevents rebinding between validation and socket
creation. Response bodies are not buffered; headers and request sizes are bounded.

Delivery is **best effort**, not a durable outbox. Read only actions timestamped
since this worker started and within the last 60 seconds, newest bounded batch
first. Late commits with older timestamps, bursts beyond the batch, downtime and
outages may lose notifications. Each observed event is attempted once; failed
requests are not replayed. The in-memory deduplication limit is 4,096 and one
checkpoint/sender may run at a time. Disposal cancels reads/HTTP without blocking
native unload. The audit database remains the authoritative history.

Test phase: use an approved disposable webhook, perform an audited action, verify
one minimal notification, outage behavior and shutdown. With `Enabled: false` no
external request or audit polling occurs. The implementation tests use mocked
transport; no message is sent to a real webhook during development.
