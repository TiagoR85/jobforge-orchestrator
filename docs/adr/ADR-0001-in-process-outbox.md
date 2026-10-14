# ADR-0001: In-process transactional outbox

**Status:** Accepted  
**Date:** 2026-10-14

## Decision

Job creation, the first execution and its outbox row are committed in one Postgres transaction; a small in-process dispatcher (`OutboxDispatcher`, batch 20 / tick 500ms) then publishes pending rows to Kafka `jobs.dispatch` (key `executionId`, `Acks=All`, idempotent producer) and only then marks them `published`. The Kafka ack is the cut line: crash before ack leaves the row `pending` for a retry (at-least-once into the topic); crash after ack can duplicate a publish, which the runner's Redis `SET NX` dedup absorbs.

## Context

The alternatives were CDC/Debezium (streaming the WAL into Kafka) and dual-write (publish to Kafka inside the request and hope). Debezium was rejected: it adds a Connect cluster, a WAL slot and operational surface that this three-service family does not justify. Dual-write was rejected because a crash between the Kafka publish and the DB commit silently loses the job. The in-process pattern matches what `einvoice-api` (F1) already proved in this portfolio.

## Consequences

- No extra infrastructure, but the outbox table grows until a purge job exists (backlog).
- Publishing throughput is bounded by one dispatcher loop per instance; multiple orchestrator replicas would each scan the same table (status transition is the race guard; acceptable at this scale).
- Ordering per execution is preserved (single partition key), cross-execution ordering is not guaranteed.
