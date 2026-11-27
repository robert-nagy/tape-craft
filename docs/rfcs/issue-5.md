# RFC: Scaffold the web app and PostgreSQL database

> Issue: #5 | Created: 2026-11-27 | Status: Implemented, in review

## Summary

Add the first runnable application: a Fastify web server in plain JavaScript (Node 22, ES modules) with a public landing page, a `/healthz` endpoint backed by PostgreSQL, SQL migrations, an installable Progressive Web App shell with an offline page, unit, integration and E2E tests, and one container image.

## Motivation

The repository has tooling but no application code (issue #5). Spotify sign-in, library sync, saving playlists and "Delete my data" each need a web server and a database. Without a shared base, each of them would build its own.

## Detailed design

**Stack.** JavaScript, matching the existing Node tooling and `.sentinel/domain.json`, so no build step is needed. `package.json` moves to `"type": "module"`. Runtime dependencies: `fastify`, `@fastify/static`, `pg`, `node-pg-migrate`.

| File | Responsibility |
| --- | --- |
| `src/config.js` | Read and validate environment variables. Fail fast with a message that never echoes a value. |
| `src/db.js` | Create the `pg` pool with short timeouts and handle idle-client errors. |
| `src/errors.js` | Turn an error into a log message, including Node's message-less `AggregateError`. |
| `src/health.js` | Database check with a hard deadline; concurrent calls share one check. |
| `src/app.js` | Build the Fastify app: static files, `/healthz`, security headers. Takes the health check as an argument so it can be tested without a database. |
| `src/server.js` | Entry point: wire the parts, listen, shut down on SIGTERM and SIGINT. Requests in progress get 5 s to finish before their connections are closed. |
| `src/migrations.js` | Run the pending migrations. |
| `src/migrate.js` | The `npm run migrate` command. |
| `migrations/*.sql` | Schema migrations. |
| `public/` | Landing page, stylesheet, web app manifest, service worker, offline page, icons. |
| `Dockerfile`, `.dockerignore` | One image that runs the server or the migrations. |

**Health check.** `SELECT 1` through the pool. The connection and query timeouts are 2 s, and an outer deadline of 2.5 s guarantees an answer within the 3 s limit even if a socket hangs. `/healthz` is public, so concurrent requests share one in-flight query and cannot exhaust the pool. The response carries no error detail; the cause is logged.

**Database outage.** The server starts and stays up without a database. The pool's `error` event is handled, so a database restart does not crash the process.

**Migrations** are a separate step (`npm run migrate`), not part of server start, so a database outage cannot stop the app from starting. `node-pg-migrate` records applied migrations and holds an advisory lock while it runs.

**PWA.** `manifest.webmanifest` declares the name, 192 px and 512 px icons, `start_url` `/` and `display: standalone`. The service worker precaches the offline page and its assets. It handles only page navigations and those assets, goes to the network first, and falls back to the cache when the request fails. It caches no application responses, so later signed-in pages are never stored.

**Security headers** on every response: a `default-src 'self'` Content-Security-Policy that also forbids framing, `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer`.

## API changes

| Route | Response |
| --- | --- |
| `GET /` | 200, public landing page |
| `GET /healthz` | 200 `{"status":"ok"}` or 503 `{"status":"unavailable"}` |
| `GET /manifest.webmanifest`, `/sw.js`, `/offline.html`, icons, stylesheet, script | 200, static |

Commands: `npm start`, `npm run migrate`. Environment variables: `DATABASE_URL` (required), `PORT` (default 3000), `HOST` (default `127.0.0.1`, which `localhost` also maps to so that there is one listener; the image sets `0.0.0.0`).

## Data changes

The first migration creates `users`, `tokens`, `tracks`, `analysis` and `playlists` with no columns, because columns are out of scope. Each feature adds its columns and keys in its own migration. `node-pg-migrate` adds its bookkeeping table `pgmigrations`.

## Test plan

- Unit: configuration validation, health check success, failure and deadline, routes and headers through `app.inject`, manifest and icon sizes, service worker behaviour.
- Integration (testcontainers, needs Docker): migrations on an empty database and a second run, `/healthz` 200 with the database up and 503 after it stops, the 3 s limit against a server that never answers, `npm start` serving `/`, and the container image running migrations and the server.
- E2E (Playwright, Chromium): landing page, no installability errors reported by Chrome, service worker control, offline page after the server process is stopped.

## Rollout and rollback

Nothing is deployed yet. Run `npm run migrate` before starting a new version. Rolling back is reverting the pull request; the empty tables can be dropped with the database.

## Risks and alternatives

- **TypeScript:** rejected for now; it adds a build step to a repository set up for JavaScript. It can be added later.
- **Express:** rejected; Fastify has built-in input validation, structured logging and request injection for tests.
- **Own migration runner:** rejected; ordering, transactions and locking are already solved by `node-pg-migrate`.
- **Migrate on server start:** rejected; it ties start-up to database availability.
- AC-8 (own window without the address bar) is a Chrome behaviour driven by `display: standalone`. The tests check the manifest and Chrome's installability report; opening the installed app needs a manual check.
- The offline page appears when the network request fails. A proxy in front of a stopped app that answers 502 is not covered; the hosting issue can revisit it.

## Open questions

None.

## Approval

Unattended run (`sentinel-sdlc-auto`): implementation proceeds without approval; review happens on the pull request.

- [ ] Design reviewed and approved. Implementation does not start until this is checked.
