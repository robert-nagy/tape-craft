# ADR 0001: Tech stack and architecture

> Status: Accepted | Date: 2026-11-27 | Decided by: the maintainer, during issue #5

## Context

tape-craft serves up to 50 listeners with low traffic. Its real logic is choosing tracks from a Liked Songs library for a playlist; the rest is calls to Spotify and storing data. What the playlist selection is based on is still open, because Spotify restricted audio features for new apps (`docs/feature-gaps-merged.md`). One maintainer works on it, so the code has to be easy to find one's way around in.

## Decision

### Stack

| Part | Choice |
| --- | --- |
| Backend | C# on .NET 10 (long-term support), ASP.NET Core minimal APIs |
| Database | PostgreSQL with EF Core and Npgsql; schema changes are EF Core migrations |
| Frontend | React with TypeScript, built with Vite; an installable Progressive Web App |
| Hosting shape | One container image. ASP.NET Core serves the built frontend from the same origin, so session cookies need no cross-site setup |
| Tests | xUnit v3 for the backend, Testcontainers for PostgreSQL, Vitest and Testing Library for the frontend, Playwright for end-to-end tests |

### Architecture: vertical slices around a pure domain

```
src/
  TapeCraft.Domain/        entities, value objects and playlist rules; plain C#, no packages
  TapeCraft.Api/           the ASP.NET Core host
    Features/<Feature>/    one folder per use case: endpoint, request and response types, handler
    Infrastructure/        adapters: Persistence (EF Core), later Spotify
    Web/                   cross-cutting HTTP concerns, such as security headers
web/src/features/<feature>/   the frontend, using the same feature names
tests/
  TapeCraft.UnitTests/         fast tests, including the architecture rules below
  TapeCraft.IntegrationTests/  against a throwaway PostgreSQL in Docker
  e2e/                         Playwright
```

Rules, enforced by architecture tests in `TapeCraft.UnitTests`:

1. `TapeCraft.Domain` references only the .NET base library: no ASP.NET Core, EF Core or Npgsql.
2. A feature does not use another feature's types. Shared rules go to the domain; shared plumbing goes to `Infrastructure` or `Web`.
3. Features depend on interfaces for the outside world (Spotify, the database, the clock); `Infrastructure` implements them.
   A feature reaches PostgreSQL through a small port shaped by its use case, such as `ISignInStore`, declared and implemented with EF Core in `Infrastructure/Persistence`. A port is not a general repository: it offers what one feature needs, no more. (Decided by the maintainer during issue #3.) The architecture tests enforce only that features use interfaces from `Infrastructure`; where a port lives and how broad it is are checked in review.

Lightweight domain-driven design: one glossary of terms (track, liked song, playlist preview), shared by code, issues and UI, and rich domain classes where rules live, such as playlist generation. Plain data handling elsewhere, with no general-purpose repositories over EF Core (use-case ports, rule 3, are not repositories), no domain events and no CQRS split.

No MediatR or AutoMapper: both moved to commercial licences in 2025, and plain handler classes with explicit mapping are clearer.

## Consequences

- One feature, one folder: an issue such as "Save a playlist" maps to `Features/Playlists/Save` and `web/src/features/playlists`.
- The selection strategy can be swapped behind an interface once the open product decision is made.
- Two toolchains (.NET and npm) to install and keep up to date; CI sets up both.
- EF Core entities need a key, so every table gets an `id` column from the start.
- From issue #3 on, ASP.NET Core Data Protection keys must be stored outside the container, or a redeploy signs users out and makes encrypted tokens unreadable.

## Alternatives considered

- **Layered (controllers, services, repositories):** familiar, but spreads each feature over every layer, and service classes tend to grow without bound.
- **Clean Architecture (Domain, Application, Infrastructure, Web projects):** the safe second choice. Rejected for now because it adds four projects and more files per feature for an app this size. The dependency rule above keeps the option to split later.
- **Modular monolith:** suited to many teams and business areas; too much for one maintainer and 50 users.
- **JavaScript with Fastify (the first draft of PR #10):** chosen without the maintainer and replaced by this decision.
