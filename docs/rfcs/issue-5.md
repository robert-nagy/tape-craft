# RFC: Scaffold the web app and PostgreSQL database

> Issue: #5 | Created: 2026-11-27 | Status: Implemented, in review

## Summary

Add the first runnable application, following ADR 0001: an ASP.NET Core backend on .NET 10 with a pure domain project, EF Core migrations for PostgreSQL, a `/healthz` endpoint, a React and TypeScript landing page that installs as a Progressive Web App with an offline page, tests on every tier, and one container image.

## Motivation

The repository has tooling but no application code (issue #5). Spotify sign-in, library sync, saving playlists and "Delete my data" each need a web server and a database. Without a shared base, each of them would build its own.

## Detailed design

Layout and rules: `docs/adr/0001-stack-and-architecture.md`.

| Path | Responsibility |
| --- | --- |
| `TapeCraft.slnx`, `Directory.Build.props`, `Directory.Packages.props` | Solution; nullable references and warnings as errors everywhere; package versions in one place |
| `src/TapeCraft.Domain/` | Entities `User`, `Token`, `Track`, `Analysis`, `Playlist`, each with only an `Id` for now |
| `src/TapeCraft.Api/Program.cs` | Entry point: `migrate` runs the migrations, anything else starts the web server |
| `src/TapeCraft.Api/AppConfig.cs` | Reads and validates `DATABASE_URL`, `PORT` and `HOST`; a bad value stops the app with a message that never includes the value |
| `src/TapeCraft.Api/Features/Health/` | `GET /healthz` and the database health check, which uses the database only through `IDatabaseProbe` |
| `src/TapeCraft.Api/Infrastructure/Persistence/` | `TapeCraftDbContext`, migrations, `NpgsqlDatabaseProbe` |
| `src/TapeCraft.Api/Web/` | Security headers |
| `web/` | Vite, React and TypeScript. `npm run build` writes to `src/TapeCraft.Api/wwwroot` |
| `web/public/` | Manifest, service worker, offline page, stylesheet, icons; served unchanged |
| `Dockerfile` | Three stages: build the frontend with Node, publish the backend with the .NET SDK, run on the ASP.NET Core runtime image as a non-root user |

**Configuration.** `DATABASE_URL` is a `postgres://user:password@host:port/database` URL, the form hosts usually provide; `sslmode` is the only query parameter accepted. It is turned into an Npgsql connection string with a 2 s connection timeout. `PORT` defaults to 3000 and `HOST` to `127.0.0.1` (the image sets `0.0.0.0`).

**Health check.** `SELECT 1` through EF Core's data source with a 2 s command timeout and an outer deadline of 2.5 s, so the response always arrives within the 3 s limit. `/healthz` is public, so concurrent requests share one database check. The response carries no error detail and is not cached; the cause is logged.

**Database outage.** The server starts and keeps running without a database, because nothing connects at start-up.

**Migrations** run with `dotnet TapeCraft.Api.dll migrate` (`npm run migrate` locally), not at server start. EF Core records applied migrations in `__EFMigrationsHistory` and takes a lock while migrating.

**PWA.** Unchanged in behaviour from the first draft: the manifest declares the name, 192 px and 512 px icons and `display: standalone`; the service worker serves the offline page when a page navigation fails and caches nothing else.

**Security headers** on every response that passes through the app: a `default-src 'self'` Content-Security-Policy that also forbids framing, `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`.

## API changes

| Route | Response |
| --- | --- |
| `GET /` | 200, public landing page |
| `GET /healthz` | 200 `{"status":"ok"}` or 503 `{"status":"unavailable"}` |
| `GET /manifest.webmanifest`, `/sw.js`, `/offline.html`, icons, stylesheet, built scripts | 200, static |

## Data changes

The first migration creates `users`, `tokens`, `tracks`, `analysis` and `playlists`, each with a `uuid` primary key `id` only. Each feature adds its columns in its own migration.

## Test plan

- Unit (`npm test`): configuration parsing, health check result, deadline and sharing, routes, static files, headers and the not-cached health response through `WebApplicationFactory`, the architecture rules; on the frontend the landing page, manifest, icons and service worker.
- Integration (`npm run test:integration`, needs Docker): migrations on an empty database and a second run, `/healthz` 200 and then 503 after the database stops, the 3 s limit against a database that never answers, the `migrate` command failing without printing the password, and the container image running migrations and the server.
- E2E (`npm run test:e2e`): landing page, no installability errors reported by Chromium, service worker control, offline page after the server process stops, and recovery.

## Rollout and rollback

Nothing is deployed yet. Run the migrations before starting a new version. Rolling back is reverting the pull request.

## Risks and alternatives

- See ADR 0001 for the stack and architecture alternatives.
- AC-8 (own window without the address bar) follows from `display: standalone`; opening the installed app needs a manual check.
- Kestrel answers malformed HTTP requests itself, before the app runs, so those responses carry no security headers. They contain no page content.

## Departures from the first draft

The first draft of this pull request used JavaScript and Fastify, chosen without the maintainer. It was replaced by ADR 0001. Tables now have an `id` column, because EF Core needs a key.

## Approval

- [x] Stack and architecture approved by the maintainer (ADR 0001).
