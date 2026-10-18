# ADR-0002: Scheduler leadership via Redis lease

**Status:** Accepted  
**Date:** 2026-10-14

## Decision

Cron ticking is leader-elected with a Redis lease: a Lua script does `SET key leaseId NX PX ttl`, renewal is a compare-and-extend Lua script (`GET == leaseId` then `PEXPIRE`), TTL ≥ 60s for a 30s tick. Only the lease holder scans `job_definitions` and fires due occurrences; a fired `(jobId, occurrence)` pair is guarded by an existing-execution check so a restart inside the tick window cannot double-fire.

## Context

Running the scheduler on every replica without coordination fires each cron slot N times. A single-instance deployment was rejected (no HA for the family's control plane). A Postgres advisory lock would work too, but Redis is already a hard dependency (runner dedup, future caches), the lease is naturally ephemeral, and the F1/F2 families already exercise StackExchange.Redis in production posture.

## Consequences

- Redis outage stops scheduling until the lease expires (executions already dispatched are unaffected).
- Clock skew between replicas matters only for the TTL; the Lua scripts are atomic on the server clock.
- Tick granularity is 30s (backlog: configurable sub-30s ticks with their duplicate-window costs).
