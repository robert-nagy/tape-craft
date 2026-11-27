import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { expect, test as base } from '@playwright/test';
import { freePort, startApp } from './server.js';

// Signing in up to the token request needs no database.
const databaseUrl = 'postgres://127.0.0.1:1/tapecraft';
const scopes = 'user-library-read playlist-modify-private playlist-modify-public playlist-read-private';

const test = base.extend({
  server: async ({}, use) => {
    const keysDir = await mkdtemp(path.join(tmpdir(), 'tape-craft-keys-'));
    const app = await startApp({ databaseUrl, port: await freePort(), keysDir });
    await use(app);
    await app.stop();
    await rm(keysDir, { recursive: true, force: true });
  },
});

/** Chooses "Sign in with Spotify" and returns the authorize URL, with Spotify's page stubbed out. */
async function chooseSignIn(page, server) {
  await page.route('https://accounts.spotify.com/**', (route) =>
    route.fulfill({ status: 200, contentType: 'text/html', body: '<title>Spotify</title><p>Spotify authorization page</p>' }),
  );
  await page.goto(server.url);
  const authorize = page.waitForRequest((request) => request.url().startsWith('https://accounts.spotify.com/'));
  await page.getByRole('link', { name: 'Sign in with Spotify' }).click();
  await page.waitForURL(/accounts\.spotify\.com/);
  return new URL((await authorize).url());
}

const cookieNamed = async (context, name) => (await context.cookies()).find((cookie) => cookie.name === name);

test('AC-1: signing in sends the listener to Spotify with four scopes, PKCE and a random state', async ({ page, context, server }) => {
  const url = await chooseSignIn(page, server);

  expect(url.origin + url.pathname).toBe('https://accounts.spotify.com/authorize');
  const query = Object.fromEntries(url.searchParams);
  expect(query).toMatchObject({
    client_id: 'e2eclientid0123',
    response_type: 'code',
    redirect_uri: `${server.url}/auth/spotify/callback`,
    scope: scopes,
    code_challenge_method: 'S256',
  });
  expect(query.code_challenge).toMatch(/^[A-Za-z0-9_-]{43}$/);
  expect(query.state).toMatch(/^[A-Za-z0-9_-]{43}$/);

  const attempt = await cookieNamed(context, 'tapecraft.signin');
  expect(attempt).toMatchObject({ httpOnly: true, secure: true, sameSite: 'Lax', path: '/auth/spotify' });
  expect(attempt.value).not.toContain(query.state);

  const again = await chooseSignIn(page, server);
  expect(again.searchParams.get('state')).not.toBe(query.state);
});

test('AC-4: denying access on Spotify shows the signed-out page with a message', async ({ page, context, server }) => {
  const url = await chooseSignIn(page, server);
  await page.goto(`${server.url}/auth/spotify/callback?error=access_denied&state=${url.searchParams.get('state')}`);

  await expect(page).toHaveURL(`${server.url}/?signin=denied`);
  await expect(page.getByRole('alert')).toHaveText(/cancelled on Spotify/);
  await expect(page.getByRole('link', { name: 'Sign in with Spotify' })).toBeVisible();
  expect(await cookieNamed(context, 'tapecraft.session')).toBeUndefined();
  expect(await cookieNamed(context, 'tapecraft.signin')).toBeUndefined();
});

test('AC-3: a state that differs from the stored one is refused with an error', async ({ page, context, server }) => {
  await chooseSignIn(page, server);
  await page.goto(`${server.url}/auth/spotify/callback?code=stolen-code&state=not-the-stored-state`);

  await expect(page).toHaveURL(`${server.url}/?signin=failed`);
  await expect(page.getByRole('alert')).toHaveText(/could not sign you in/);
  expect(await cookieNamed(context, 'tapecraft.session')).toBeUndefined();
});

test('AC-3: a callback without any state is refused', async ({ page, context, server }) => {
  await page.goto(`${server.url}/auth/spotify/callback?code=stolen-code`);
  await expect(page.getByRole('alert')).toHaveText(/could not sign you in/);
  expect(await cookieNamed(context, 'tapecraft.session')).toBeUndefined();
});
