# ADR-0003: OpenIddict in the primary Postgres

**Status:** Accepted  
**Date:** 2026-10-14

## Decision

The orchestrator is its own OIDC issuer: OpenIddict 7.7 server (client-credentials only) with the application/scope stores in the same Postgres database (`UseOpenIddict()` on the main context, one migration). Tokens are validated with `UseLocalServer()` (no discovery round-trip, works at any test port). Scopes `jobs:read`/`jobs:write` are registered server-side; clients `jobforge-demo` and `jobforge-console` are seeded in Development with permission rows per scope.

## Context

The rejected alternatives were an external issuer (F1's einvoice-api) and a separate SQLite store (logpipe-ingest). Reusing F1 as issuer couples the jobforge demo to another deployment for zero narrative gain, and a second database file just to hold two OAuth clients is pure operational noise when the primary Postgres is already there and migrations run at startup.

## Consequences

- Deleting the database also deletes the OAuth clients; the Development seed restores them.
- Development certificates are generated per host; rotating them invalidates outstanding ~5-minute tokens only.
- Cross-service token validation (UI, runner) uses standard discovery against the orchestrator; production would add HTTPS + audiences (roadmap).
