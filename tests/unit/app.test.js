import { afterEach, describe, expect, it, vi } from 'vitest';
import { buildApp } from '../../src/app.js';

let app;
const build = (checkDatabase = async () => true) => (app = buildApp({ checkDatabase }));

afterEach(() => app?.close());

describe('GET /', () => {
  it('serves the public landing page without sign-in, even when the database is down', async () => {
    const checkDatabase = vi.fn(async () => false);
    const response = await build(checkDatabase).inject({ url: '/' });
    expect(response.statusCode).toBe(200);
    expect(response.headers['content-type']).toMatch(/^text\/html/);
    expect(response.body).toContain('<h1>tape-craft</h1>');
    expect(checkDatabase).not.toHaveBeenCalled();
  });
});

describe('GET /healthz', () => {
  it('answers 200 when the database is reachable', async () => {
    const response = await build(async () => true).inject({ url: '/healthz' });
    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ status: 'ok' });
    expect(response.headers['cache-control']).toBe('no-store');
  });

  it('answers 503 when the database is unreachable', async () => {
    const response = await build(async () => false).inject({ url: '/healthz' });
    expect(response.statusCode).toBe(503);
    expect(response.json()).toEqual({ status: 'unavailable' });
    expect(response.headers['cache-control']).toBe('no-store');
  });
});

describe('static files', () => {
  it.each([
    ['/manifest.webmanifest', /^application\/manifest\+json/],
    ['/sw.js', /javascript/],
    ['/app.js', /javascript/],
    ['/styles.css', /^text\/css/],
    ['/offline.html', /^text\/html/],
    ['/icons/icon-192.png', /^image\/png/],
    ['/icons/icon-512.png', /^image\/png/],
  ])('serves %s', async (url, type) => {
    const response = await build().inject({ url });
    expect(response.statusCode).toBe(200);
    expect(response.headers['content-type']).toMatch(type);
  });

  it.each(['/missing', '/../package.json', '/%2e%2e/package.json', '/..%2fpackage.json'])(
    'does not serve %s',
    async (url) => {
      const response = await build().inject({ url });
      expect(response.statusCode).toBeGreaterThanOrEqual(400);
      expect(response.body).not.toContain('"dependencies"');
    },
  );
});

describe('a malformed URL', () => {
  it('is rejected without echoing it', async () => {
    const response = await build().inject({ url: '/%E0%A4%A' });
    expect(response.statusCode).toBe(400);
    expect(response.json()).toEqual({ error: 'Bad Request' });
  });
});

describe('security headers', () => {
  it.each(['/', '/healthz', '/missing', '/%'])('are set on %s', async (url) => {
    const { headers } = await build().inject({ url });
    expect(headers['content-security-policy']).toBe(
      "default-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'",
    );
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['referrer-policy']).toBe('no-referrer');
  });
});
