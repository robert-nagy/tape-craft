import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const source = await readFile(new URL('../../public/sw.js', import.meta.url), 'utf8');

const ORIGIN = 'https://tape.example';
let listeners;
let stores;
let fetchMock;
let self;

const pathOf = (key) => new URL(typeof key === 'string' ? key : key.url, ORIGIN).pathname;

beforeEach(() => {
  listeners = {};
  stores = new Map();
  fetchMock = vi.fn();
  const caches = {
    open: async (name) => {
      if (!stores.has(name)) stores.set(name, new Map());
      const store = stores.get(name);
      return {
        addAll: async (requests) => requests.forEach((r) => store.set(pathOf(r), `cached ${pathOf(r)}`)),
        match: async (key) => store.get(pathOf(key)),
      };
    },
    keys: async () => [...stores.keys()],
    delete: async (name) => stores.delete(name),
  };
  self = {
    location: { origin: ORIGIN },
    addEventListener: (type, listener) => (listeners[type] = listener),
    skipWaiting: vi.fn(async () => {}),
    clients: { claim: vi.fn(async () => {}) },
  };
  class FakeRequest {
    constructor(path, options) {
      Object.assign(this, { url: new URL(path, ORIGIN).href }, options);
    }
  }
  vm.runInNewContext(source, { self, caches, fetch: fetchMock, Request: FakeRequest, Response, URL });
});

async function lifecycle(type) {
  let work;
  listeners[type]({ waitUntil: (promise) => (work = promise) });
  await work;
}

function request(path, { mode = 'no-cors', method = 'GET', origin = ORIGIN } = {}) {
  let response;
  const req = { url: origin + path, mode, method };
  listeners.fetch({ request: req, respondWith: (promise) => (response = promise) });
  return { req, response };
}

describe('service worker', () => {
  it('precaches only the offline page and its assets, then takes over', async () => {
    await lifecycle('install');
    const [store] = stores.values();
    expect([...store.keys()]).toEqual(['/offline.html', '/styles.css', '/icons/icon-192.png']);
    expect(self.skipWaiting).toHaveBeenCalled();
  });

  it('removes only its own outdated caches on activation', async () => {
    stores.set('tape-craft-offline-v0', new Map());
    stores.set('someone-else', new Map());
    await lifecycle('install');
    await lifecycle('activate');
    expect([...stores.keys()]).toEqual(['someone-else', 'tape-craft-offline-v1']);
    expect(self.clients.claim).toHaveBeenCalled();
  });

  it('returns the network response for a navigation and stores nothing', async () => {
    await lifecycle('install');
    const before = [...stores.values()][0].size;
    fetchMock.mockResolvedValue('from network');
    await expect(request('/', { mode: 'navigate' }).response).resolves.toBe('from network');
    expect([...stores.values()][0].size).toBe(before);
  });

  it('shows the offline page when a navigation cannot reach the server', async () => {
    await lifecycle('install');
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(request('/playlists', { mode: 'navigate' }).response).resolves.toBe('cached /offline.html');
  });

  it('serves the offline page assets from the cache when the server is unreachable', async () => {
    await lifecycle('install');
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(request('/styles.css').response).resolves.toBe('cached /styles.css');
  });

  it('still explains the problem if the cached page is gone', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const response = await request('/', { mode: 'navigate' }).response;
    expect(response.status).toBe(503);
    await expect(response.text()).resolves.toContain('cannot be reached');
  });

  it.each([
    ['another request', '/api/tracks', {}],
    ['another origin', '/styles.css', { origin: 'https://other.example' }],
    ['a form submission', '/', { mode: 'navigate', method: 'POST' }],
  ])('leaves %s to the browser', (_label, path, options) => {
    expect(request(path, options).response).toBeUndefined();
  });
});
