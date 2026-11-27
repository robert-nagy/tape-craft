import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { chromium, expect, test as base } from '@playwright/test';
import { freePort, startApp } from './server.js';

// The pages under test do not need a database, so none is started.
const databaseUrl = 'postgres://127.0.0.1:1/tapecraft';

const test = base.extend({
  server: async ({}, use) => {
    const port = await freePort();
    let app = await startApp({ databaseUrl, port });
    await use({
      url: app.url,
      stop: () => app.stop(),
      start: async () => (app = await startApp({ databaseUrl, port })),
    });
    await app.stop();
  },
  // Chrome never offers installation in an incognito-like context, so use a real profile.
  context: async ({}, use) => {
    const profile = await mkdtemp(path.join(tmpdir(), 'tape-craft-'));
    const context = await chromium.launchPersistentContext(profile, { channel: 'chromium' });
    await use(context);
    await context.close();
    await rm(profile, { recursive: true, force: true });
  },
});

async function openControlledPage(context, url) {
  const page = await context.newPage();
  await page.goto(url);
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
  return page;
}

test('shows the public landing page', async ({ context, server }) => {
  const page = await context.newPage();
  const errors = [];
  page.on('console', (message) => message.type() === 'error' && errors.push(message.text()));
  const response = await page.goto(server.url);
  expect(response.status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1, name: 'tape-craft' })).toBeVisible();
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
  expect(errors).toEqual([]);
});

test('Chrome finds nothing that stops it being installed as a standalone app', async ({ context, server }) => {
  const page = await openControlledPage(context, server.url);
  const session = await context.newCDPSession(page);
  await session.send('Page.enable');

  const manifest = await session.send('Page.getAppManifest');
  expect(manifest.errors).toEqual([]);
  expect(JSON.parse(manifest.data)).toMatchObject({ name: 'tape-craft', display: 'standalone' });
  await expect
    .poll(async () => (await session.send('Page.getInstallabilityErrors')).installabilityErrors)
    .toEqual([]);
});

test('shows its own page while the server cannot be reached, and recovers', async ({ context, server }) => {
  const page = await openControlledPage(context, server.url);

  await server.stop();
  await page.reload();
  const heading = page.getByRole('heading', { name: 'The server cannot be reached' });
  await expect(heading).toBeVisible();
  // The stylesheet comes from the service worker cache.
  await expect(page.locator('main')).toHaveCSS('text-align', 'center');

  await server.start();
  await page.getByRole('link', { name: 'Try again' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'tape-craft' })).toBeVisible();
});
