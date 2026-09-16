# Security policy

AnoCore is currently under private development.

Do not commit server passwords, Steam credentials, database passwords, API keys, webhook secrets or production connection strings. Use environment variables or deployment-time secret storage.

Security-sensitive changes require explicit review of authorization, input validation, SQL/query parameterization, logging/redaction and failure behavior.

When the project becomes public, a private vulnerability reporting channel should be enabled before the first public release.
