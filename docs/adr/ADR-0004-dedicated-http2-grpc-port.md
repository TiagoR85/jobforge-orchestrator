# ADR-0004: Dedicated HTTP/2-only port for gRPC

**Status:** Accepted  
**Date:** 2026-10-14

## Decision

Kestrel exposes two endpoints: `http` on :8094 with `Protocols=Http1` (REST + OIDC) and `grpc` on :8097 with `Protocols=Http2` (cleartext h2c, the runner's status stream). The regression is locked by `KestrelEndpointConfigTests`, which asserts both URL and protocol settings in `appsettings.json`.

## Context

This is the family's documented lesson: sharing a port between REST and gRPC without an explicit HTTP/2 endpoint makes clients negotiate badly and the server answers `GOAWAY HTTP_1_1_REQUIRED` (dotnet/aspnetcore#56984 — the story from logpipe's F2). TLS-terminating gRPC internally was rejected for the demo: the compose network is trusted, and certificates would complicate the one-command story; mTLS stays in the backlog for production.

## Consequences

- Anything speaking gRPC must target :8097 explicitly; REST clients never see HTTP/2.
- The in-process test host bypasses Kestrel and speaks h2c directly, so tests do not depend on the port numbers — the config test carries that contract instead.
- Adding HTTP/3 or enabling TLS later touches only Kestrel config, not the services.
