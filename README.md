# tape-craft

Tool using Spotify API that makes sense of your Liked Songs and crafts playlists based on your needs.

## Problem

A Spotify user's Liked Songs grows into one huge unsorted list. Sorting it into mood, genre or activity playlists by hand is tedious, and Spotify offers no tool for it. tape-craft analyses the Liked Songs library and crafts playlists based on what the user needs.

## Users

- Listener: the owner or a friend who signs in with their own Spotify account to organise their own Liked Songs into playlists
- Owner: runs and maintains the app and its Spotify developer app registration (through server access)

## Core user journeys

- Sign in with Spotify.
- Sync and analyse Liked Songs.
- Describe a need (mood, genre or activity) and preview a generated playlist.
- Save the playlist to the user's Spotify account.
- Revisit and regenerate saved playlists.

## Getting started

You need the .NET 10 SDK, Node.js 22.22 or later, a PostgreSQL database, and a Spotify app (see [Spotify app](#spotify-app)).

```sh
npm ci
export DATABASE_URL='postgres://USER:PASSWORD@localhost:5432/tapecraft'
export SPOTIFY_CLIENT_ID='your Spotify app client ID'
export SPOTIFY_REDIRECT_URI='http://127.0.0.1:3000/auth/spotify/callback'
export DATA_PROTECTION_KEYS_DIR="$HOME/.local/share/tape-craft/keys"
npm run migrate
npm start
```

`npm start` builds the React frontend and runs the backend (`dotnet run --project src/TapeCraft.Api`). Then open <http://127.0.0.1:3000>, the address the redirect URI names, so that the browser keeps the sign-in cookie. In Google Chrome, use the install button in the address bar to add tape-craft as an app. While working on the frontend, `npm run dev --workspace web` serves it with hot reload on <http://localhost:5173>. Sign-in does not work there: Spotify returns to `SPOTIFY_REDIRECT_URI` on port 3000, where the browser has no sign-in cookie. Try signing in on <http://127.0.0.1:3000>.

For a throwaway local database:

```sh
export POSTGRES_PASSWORD="$(openssl rand -hex 16)"
docker run --rm -d --name tape-craft-db -e POSTGRES_PASSWORD -e POSTGRES_DB=tapecraft -p 127.0.0.1:5432:5432 postgres:17-alpine
export DATABASE_URL="postgres://postgres:$POSTGRES_PASSWORD@localhost:5432/tapecraft"
until docker exec tape-craft-db pg_isready -h 127.0.0.1 -U postgres; do sleep 1; done
```

Then run `npm run migrate` and `npm start` as above.

### Project structure

The stack and architecture are recorded in [ADR 0001](docs/adr/0001-stack-and-architecture.md): vertical slices around a pure domain.

| Path | What lives there |
| --- | --- |
| `src/TapeCraft.Domain/` | Entities and playlist rules, in plain C# |
| `src/TapeCraft.Api/Features/` | One folder per use case, such as `Health/` |
| `src/TapeCraft.Api/Infrastructure/` | Database access (EF Core) and, later, the Spotify client |
| `src/TapeCraft.Api/Web/` | Cross-cutting HTTP concerns, such as security headers |
| `web/` | React and TypeScript frontend; `web/src/features/` uses the same feature names |
| `tests/` | Backend unit and integration tests, and Playwright tests in `tests/e2e/` |

Architecture tests fail the build when the domain uses a framework, or one feature uses another.

To change the database schema, edit the entities, then run `dotnet ef migrations add <Name> --project src/TapeCraft.Api --output-dir Infrastructure/Persistence/Migrations` (after `dotnet tool restore`).

### Configuration

All configuration comes from environment variables. Never commit credentials.

| Variable | Required | Default | Purpose |
| --- | --- | --- | --- |
| `DATABASE_URL` | yes | | PostgreSQL URL, `postgres://user:password@host:port/database`; `sslmode` (for example `?sslmode=require`) is the only query parameter accepted |
| `PORT` | no | `3000` | Port to listen on |
| `HOST` | no | `127.0.0.1` (`0.0.0.0` in the container image) | IP address to listen on; `localhost` means `127.0.0.1` |
| `SPOTIFY_CLIENT_ID` | yes, for the web server | | Client ID of the Spotify app. No client secret is needed: sign-in uses PKCE |
| `SPOTIFY_REDIRECT_URI` | yes, for the web server | | The address Spotify returns to, ending in `/auth/spotify/callback`; `https`, or `http` on `127.0.0.1` or `[::1]` (Spotify does not accept `localhost`). It must match a redirect URI of the Spotify app exactly |
| `DATA_PROTECTION_KEYS_DIR` | yes, for the web server | | Absolute path of the folder for the key ring that encrypts sessions and stored Spotify tokens. Keep it outside the repository and the container, readable only by the app, and back it up: losing it signs everyone out and makes stored tokens unreadable |

`npm run migrate` (and `migrate` in the container) needs only `DATABASE_URL`.

### Spotify app

1. Create an app in the [Spotify developer dashboard](https://developer.spotify.com/dashboard) and select the Web API. A new app runs in Development Mode: at most 5 users, each added by Spotify account in the dashboard, and the app's owner needs Spotify Premium.
2. Add the redirect URI, for example `http://127.0.0.1:3000/auth/spotify/callback` locally, or `https://<your host>/auth/spotify/callback`.
3. Set `SPOTIFY_CLIENT_ID` to the app's client ID and `SPOTIFY_REDIRECT_URI` to the same redirect URI.

### Sign-in

Listeners sign in with the authorization code flow with PKCE. tape-craft asks only for `user-library-read`, `playlist-modify-private`, `playlist-modify-public` and `playlist-read-private`.

| Route | What it does |
| --- | --- |
| `GET /auth/spotify/sign-in` | Redirects to Spotify with a PKCE challenge and a random state, which it keeps in an encrypted 10-minute cookie |
| `GET /auth/spotify/callback` | Refuses a missing or wrong state without calling Spotify, exchanges the code, and starts the session; redirects to `/`, `/?signin=denied` or `/?signin=failed` |
| `GET /api/session` | `{"signedIn":false}` or `{"signedIn":true,"name":"…"}` |

The session cookie `tapecraft.session` is HttpOnly, Secure and SameSite=Lax, lasts 14 days from the last visit, and holds no Spotify token. Access and refresh tokens are stored in the `tokens` table, encrypted with ASP.NET Core Data Protection. Browsers accept Secure cookies over plain HTTP only on `localhost` and `127.0.0.1`, so a deployed app needs HTTPS.

### Health check

`GET /healthz` answers within 3 seconds: 200 when the database is reachable, 503 when it is not.

### Tests

| Command | Runs | Needs |
| --- | --- | --- |
| `npm test` | frontend tests (Vitest), then backend unit and architecture tests (xUnit), including sign-in against a fake Spotify | |
| `npm run test:integration` | migrations, health check, shutdown, stored sign-ins and token encryption, and the container image, against a throwaway PostgreSQL | Docker |
| `npm run test:e2e` | landing page, installability, offline page and the sign-in redirect and errors in Chromium, against the published app (Spotify's page is stubbed) | the .NET SDK, and `npx playwright install chromium` once |

### Container

One image runs both the migrations and the app. With `DATABASE_URL` set to an address the container can reach, and the Spotify settings exported:

```sh
docker build -t tape-craft .
docker run --rm -e DATABASE_URL tape-craft migrate
docker volume create tape-craft-keys
docker run --rm -e DATABASE_URL -e SPOTIFY_CLIENT_ID -e SPOTIFY_REDIRECT_URI \
  -e DATA_PROTECTION_KEYS_DIR=/keys -v tape-craft-keys:/keys -p 3000:3000 tape-craft
```

The key ring lives in the volume, so a new image keeps listeners signed in and their tokens readable.

Chrome offers installation only over HTTPS or on `localhost`, so a deployed app needs HTTPS.

## Data

Spotify OAuth access and refresh tokens per user; Liked Songs metadata (track, album and artist IDs and names, genres, audio features); playlists the app creates in the user's Spotify account.

## Scale and service levels

Up to 50 users, each with up to 10,000 Liked Songs; low traffic, well under 1 request per second; Spotify Web API rate limits are the main constraint.

99% monthly uptime; web UI pages p95 under 500 ms; generating a playlist from a 10,000-song library under 30 s.

RPO 24 hours (daily database backups); RTO 4 hours. Liked Songs data can also be re-fetched from Spotify.

## Security

**Authentication.** Users sign in with Spotify OAuth 2.0 authorization code flow with PKCE; the app identifies itself to Spotify with its client ID and needs no client secret; the app session is kept in a secure cookie.

**Authorisation.** One role, Listener: each user may read only their own Liked Songs and analysis and create, view or manage only playlists in their own Spotify account. The owner administers the app through server access; there is no admin role or UI.

**Boundaries.** Public: landing page and Spotify sign-in. Signed-in only: web UI and API. No admin-only area. Untrusted input enters through user form and API input (playlist criteria, playlist names), OAuth callback parameters, and Spotify API responses.

## Integrations

- Spotify Accounts service (OAuth sign-in and token refresh)
- Spotify Web API (Liked Songs and audio features and playlists)

## Compliance

GDPR (personal data of EU users) and the Spotify Developer Terms and Developer Policy.

Personal data (Spotify user ID, profile, OAuth tokens, cached library) is kept in the app database while the user is active. 'Delete my data' removes tokens and cached library immediately. Accounts inactive for 12 months are purged. Backups expire within 30 days.

## Architecture

### System context

```mermaid
flowchart LR
  system["tape-craft"]
  u0["Listener: the owner or a friend who sig…"]
  u0 --> system
  u1["Owner: runs and maintains the app and i…"]
  u1 --> system
  x0["Spotify Accounts service OAuth sign-in…"]
  system --> x0
  x1["Spotify Web API Liked Songs and audio f…"]
  system --> x1
```

### Containers

```mermaid
flowchart TB
  subgraph system["tape-craft"]
  c0["tape-craft: ASP.NET Core, React"]
  end
  store[("PostgreSQL")]
  c0 --> store
```

---

_Generated by `sentinel setup` from `.sentinel/domain.json`._
