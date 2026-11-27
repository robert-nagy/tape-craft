# RFC: Connect a Spotify account at sign-in

> Issue: #3 | Created: 2026-11-27 | Status: Implemented, in review

## Summary

Listeners sign in with Spotify using the OAuth 2.0 authorization code flow with PKCE. The app asks for exactly four scopes, checks a random `state` value against one kept in an encrypted short-lived cookie, exchanges the code with the PKCE verifier, stores the listener and their tokens (encrypted with ASP.NET Core Data Protection) in PostgreSQL, and keeps the session in an HttpOnly, Secure, SameSite=Lax cookie that carries no Spotify token. The landing page offers "Sign in with Spotify", shows who is signed in, and explains a cancelled or failed sign-in.

## Motivation

Every Spotify feature on the roadmap (#4, #6 to #9) needs the listener's Spotify account. Until sign-in exists, no listener can get a playlist from their own library. Issue #5 (web app and database) is merged, so this is the next step.

## Detailed design

Follows ADR 0001: one feature folder, the outside world behind interfaces, rules in the domain.

| Path | Responsibility |
| --- | --- |
| `src/TapeCraft.Domain/User.cs` | `User.Register(profile, now)`: Spotify user ID (required), display name, creation time; `UpdateProfile` on a later sign-in |
| `src/TapeCraft.Domain/Token.cs` | `Token.Issue(userId, grant, now)` and `Replace(grant, now)`: access token, refresh token, expiry = now + `expires_in` |
| `src/TapeCraft.Domain/Spotify.cs` | Value objects `SpotifyGrant` (tokens and lifetime) and `SpotifyProfile` (user ID, display name) |
| `src/TapeCraft.Api/SignInConfig.cs` | Reads and validates `SPOTIFY_CLIENT_ID`, `SPOTIFY_REDIRECT_URI` and `DATA_PROTECTION_KEYS_DIR`; errors never include a value |
| `src/TapeCraft.Api/Features/SignIn/` | `GET /auth/spotify/sign-in`, `GET /auth/spotify/callback`, `GET /api/session`; PKCE and state generation; the sign-in attempt cookie |
| `src/TapeCraft.Api/Infrastructure/Spotify/` | `ISpotify` and `SpotifyClient` (typed `HttpClient`): authorize URL, token exchange, `GET /v1/me` |
| `src/TapeCraft.Api/Infrastructure/Persistence/` | `ISignInStore` and `EfSignInStore` (create or update the user and their token row); EF mapping with a Data Protection value converter on both token columns; migration `AddSignIn` |
| `src/TapeCraft.Api/Web/Session.cs` | Data Protection key ring on disk, and cookie authentication with the session cookie settings |
| `web/src/features/sign-in/` | `SignIn` component and `fetchSession`; the landing page shows it |

The feature uses `ISpotify` and `ISignInStore`, not `TapeCraftDbContext`, because the architecture tests forbid features from depending on Infrastructure classes (the same reason Health uses `IDatabaseProbe`). `ISignInStore` has one method shaped by the use case, not a general repository. The maintainer approved this as the pattern for every feature (ADR 0001, rule 3).

**Start (`GET /auth/spotify/sign-in`).** Generates a 32-byte random `state` and a 64-byte random code verifier (86 base64url characters), and derives the S256 challenge. It stores `{state, verifier}` in the cookie `tapecraft.signin`, protected with a time-limited Data Protection protector (10 minutes). The cookie is HttpOnly, Secure and SameSite=Lax, with path `/auth/spotify`. Then it redirects (302) to `https://accounts.spotify.com/authorize` with `client_id`, `response_type=code`, `redirect_uri`, `scope=user-library-read playlist-modify-private playlist-modify-public playlist-read-private`, `code_challenge_method=S256`, `code_challenge` and `state`.

**Callback (`GET /auth/spotify/callback`).** Always deletes the attempt cookie first, then:

1. No readable attempt cookie, or no `state`, or a `state` that differs from the stored one (compared in fixed time): redirect to `/?signin=failed`. No token request is made.
2. An `error` parameter (for example `access_denied`): redirect to `/?signin=denied`.
3. No `code`: `/?signin=failed`.
4. Exchange the code at `https://accounts.spotify.com/api/token` (`grant_type=authorization_code`, `code`, `redirect_uri`, `client_id`, `code_verifier`), then read the profile with `GET https://api.spotify.com/v1/me`. Any failure is logged without tokens or codes and redirects to `/?signin=failed`.
5. Save the user and token, sign in with cookie authentication, and redirect to `/`.

Checking `state` before `error` means a forged "denied" callback is also refused.

**Session.** ASP.NET Core cookie authentication, cookie `tapecraft.session`: HttpOnly, `Secure` always, SameSite=Lax, path `/`, 14-day sliding expiry. The cookie holds only the user's ID and display name as claims, encrypted by Data Protection. Tokens stay in the database. `GET /api/session` answers `{"signedIn":false}` or `{"signedIn":true,"name":"…"}`, with `Cache-Control: no-store`.

**Token encryption.** An EF Core value converter protects `access_token` and `refresh_token` with the Data Protection purpose `TapeCraft.Tokens` on write and unprotects them on read. The domain keeps plain values in memory. The key ring is stored on disk in `DATA_PROTECTION_KEYS_DIR` with application name `tape-craft`, as ADR 0001 asks, so a redeploy neither signs listeners out nor makes the stored tokens unreadable. The directory has to be outside the repository and the container image.

**Configuration** is validated when the web server starts, not for `migrate`, so migrations need no Spotify settings. The web server also loads or creates the key ring at start-up, so an unusable `DATA_PROTECTION_KEYS_DIR` stops it with a message instead of failing every sign-in. A plain `http` redirect URI must point to `127.0.0.1` or `[::1]`, because Spotify does not accept `localhost`.

**Frontend.** The landing page fetches `/api/session`. Signed out, it shows a "Sign in with Spotify" link to `/auth/spotify/sign-in`. Signed in, it shows "Signed in as <name>". `?signin=denied` and `?signin=failed` show a fixed message; the page never displays text from the URL.

## API changes

| Route | Response |
| --- | --- |
| `GET /auth/spotify/sign-in` | 302 to Spotify's authorize page, sets `tapecraft.signin` |
| `GET /auth/spotify/callback` | 302 to `/` (signed in), `/?signin=denied` or `/?signin=failed`; deletes `tapecraft.signin` |
| `GET /api/session` | 200 `{"signedIn":false}` or `{"signedIn":true,"name":"…"}`, not cached |

New required environment variables for the web server: `SPOTIFY_CLIENT_ID`, `SPOTIFY_REDIRECT_URI`, `DATA_PROTECTION_KEYS_DIR`.

## Data changes

Migration `AddSignIn`:

- `users`: `spotify_user_id text not null` (unique), `display_name text null`, `created_at timestamptz not null`.
- `tokens`: `user_id uuid not null` (unique, foreign key to `users`, cascade delete), `access_token text not null`, `refresh_token text not null` (both Data Protection ciphertext), `expires_at timestamptz not null`.

Both tables are empty before this change, so the migration needs no backfill.

## Test plan

- Unit (`npm test`): domain rules for `User` and `Token`; PKCE verifier length and S256 challenge; the authorize URL with exactly the four scopes; `SignInConfig` validation; the token converter (ciphertext differs from the plain value and round-trips). Through `WebApplicationFactory` with a fake `ISpotify` and `ISignInStore`: AC-1 redirect and attempt cookie attributes; AC-2 exchange with the stored verifier, session created, `/api/session` signed in; AC-3 a wrong, missing or cookie-less state makes no Spotify call and stores nothing; AC-4 a denied callback stores nothing and sets no session; AC-5 session cookie attributes and no token in the cookie value; a failing exchange. Frontend: the sign-in link, the signed-in name, and both error messages.
- Integration (`npm run test:integration`, Docker): the migration creates the new columns and the unique indexes; AC-6 a sign-in through the real app and PostgreSQL stores tokens whose raw column values are not the tokens, and a second sign-in updates the same rows; tokens remain readable by a new app instance using the same key directory; the web server refuses to start without a sign-in setting or with an unusable key directory; the container image starts with the new variables and writes its key ring to `/keys`.
- E2E (`npm run test:e2e`): AC-1 in Chromium, with Spotify's authorize page intercepted: the scopes, S256 challenge and state; AC-3 and AC-4 in the browser: the error message is shown and no session cookie exists. A full successful sign-in needs a real Spotify account, so AC-2, AC-5 and AC-6 are covered by unit and integration tests with a fake Spotify.

## Rollout and rollback

Nothing is deployed yet. Before deploying, register a Spotify app (Development Mode, at most 5 users, owned by a Premium account), add the redirect URI, set the three variables, and mount a persistent directory for `DATA_PROTECTION_KEYS_DIR`. Run `migrate` before starting the new version. Rolling back means reverting the pull request; the migration's `Down` drops the new columns.

## Risks and alternatives

- **Client secret.** Spotify's PKCE flow sends `client_id` and `code_verifier` and no secret, so tape-craft needs no client secret and stores none. The README's Security section and `.sentinel/domain.json` used to say otherwise; the maintainer chose no secret, and both now match.
- **Keys at rest.** On Linux, the Data Protection key ring is stored unencrypted in its directory. Anyone who can read that directory and the database can decrypt the tokens. Keep the directory readable only by the app user, and back it up separately from the database.
- **Sign-in state in a cookie, not a server session store.** An encrypted, time-limited cookie needs no new store and survives a restart. Session middleware with a memory cache was rejected because it would lose sign-ins in progress on restart and adds a second session cookie.
- **Concurrent first sign-ins of the same Spotify user** can violate the unique index; the second one sees "sign-in failed" and can retry.
- **Secure cookies over plain HTTP** are accepted by browsers only on `localhost` and `127.0.0.1`. A deployed app needs HTTPS, as the README already says for installation.
- **EF Core model caching.** EF Core builds the model, and so the token converter, once per cache key. `ProtectionModelCacheKeyFactory` adds the Data Protection provider to the key, so every key ring gets its own converter. Production has one provider; the cache grows only in processes that create many, as the tests do.

## Open questions

None. Two were settled by the maintainer during the run:

- Features reach PostgreSQL through use-case ports in `Infrastructure/Persistence`, such as `ISignInStore`; recorded in ADR 0001, rule 3.
- No client secret: PKCE only.

## Approval

- [ ] Design reviewed and approved. Unattended run (`sentinel-sdlc-auto`): implemented without waiting for approval. On 2026-11-27 the maintainer settled only the two open questions above; the design as a whole awaits approval in the pull request.
