import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest';

// public/sw.js is plain JavaScript served as is; run it in a sandbox with fake browser APIs.
const source = await readFile(new URL('../public/sw.js', import.meta.url), 'utf8');

const ORIGIN = 'https://tape.example';

type Listener = (event: Record<string, unknown>) => void;
type Store = Map<string, string>;

let listeners: Record<string, Listener>;
let stores: Map<string, Store>;
let fetchMock: Mock;
let self: { skipWaiting: Mock; clients: { claim: Mock } };

const pathOf = (key: string | { url: string }) => new URL(typeof key === 'string' ? key : key.url, ORIGIN).pathname;

beforeEach(() => {
  listeners = {};
  stores = new Map();
  fetchMock = vi.fn();
  const caches = {
    open: async (name: string) => {
      const store = stores.get(name) ?? new Map<string, string>();
      stores.set(name, store);
      return {
        addAll: async (requests: { url: string }[]) =>
          requests.forEach((r) => store.set(pathOf(r), `cached ${pathOf(r)}`)),
        match: async (key: string | { url: string }) => store.get(pathOf(key)),
      };
    },
    keys: async () => [...stores.keys()],
    delete: async (name: string) => stores.delete(name),
  };
  self = { skipWaiting: vi.fn(async () => {}), clients: { claim: vi.fn(async () => {}) } };
  class FakeRequest {
    url: string;
    constructor(path: string, options: object) {
      this.url = new URL(path, ORIGIN).href;
      Object.assign(this, options);
    }
  }
  const sandboxSelf = {
    ...self,
    location: { origin: ORIGIN },
    addEventListener: (type: string, listener: Listener) => (listeners[type] = listener),
  };
  vm.runInNewContext(source, { self: sandboxSelf, caches, fetch: fetchMock, Request: FakeRequest, Response, URL });
});

const listener = (type: string): Listener => {
  const found = listeners[type];
  if (!found) throw new Error(`The service worker does not listen for ${type}`);
  return found;
};

async function lifecycle(type: 'install' | 'activate') {
  let work: Promise<unknown> | undefined;
  listener(type)({ waitUntil: (promise: Promise<unknown>) => (work = promise) });
  await work;
}

function request(path: string, { mode = 'no-cors', method = 'GET', origin = ORIGIN } = {}) {
  let response: Promise<unknown> | undefined;
  listener('fetch')({
    request: { url: origin + path, mode, method },
    respondWith: (promise: Promise<unknown>) => (response = promise),
  });
  return response;
}

const onlyStore = () => [...stores.values()][0] ?? new Map();

describe('service worker', () => {
  it('precaches only the offline page and its assets, then takes over', async () => {
    await lifecycle('install');
    expect([...onlyStore().keys()]).toEqual(['/offline.html', '/styles.css', '/icons/icon-192.png']);
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
    const before = onlyStore().size;
    fetchMock.mockResolvedValue('from network');
    await expect(request('/', { mode: 'navigate' })).resolves.toBe('from network');
    expect(onlyStore().size).toBe(before);
  });

  it('shows the offline page when a navigation cannot reach the server', async () => {
    await lifecycle('install');
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(request('/playlists', { mode: 'navigate' })).resolves.toBe('cached /offline.html');
  });

  it('serves the offline page assets from the cache when the server is unreachable', async () => {
    await lifecycle('install');
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(request('/styles.css')).resolves.toBe('cached /styles.css');
  });

  it('still explains the problem if the cached page is gone', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const response = (await request('/', { mode: 'navigate' })) as Response;
    expect(response.status).toBe(503);
    await expect(response.text()).resolves.toContain('cannot be reached');
  });

  it.each([
    ['another request', '/api/tracks', {}],
    ['another origin', '/styles.css', { origin: 'https://other.example' }],
    ['a form submission', '/', { mode: 'navigate', method: 'POST' }],
  ])('leaves %s to the browser', (_label, path, options) => {
    expect(request(path, options)).toBeUndefined();
  });
});
