import { execFile } from 'node:child_process';
import net from 'node:net';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { freePort, startApp } from '../helpers/server.js';

// A database host that accepts the connection and then says nothing.
const sockets = new Set();
const blackHole = net.createServer((socket) => sockets.add(socket));

beforeAll(() => new Promise((resolve) => blackHole.listen(0, '127.0.0.1', resolve)));

afterAll(() => {
  sockets.forEach((socket) => socket.destroy());
  return new Promise((resolve) => blackHole.close(resolve));
});

describe.each([
  ['refuses connections', async () => freePort()],
  ['never answers', async () => blackHole.address().port],
])('when the database %s', (_label, databasePort) => {
  it('starts, answers /healthz with 503 within 3 seconds and stops cleanly', async () => {
    const databaseUrl = `postgres://127.0.0.1:${await databasePort()}/tapecraft`;
    const app = await startApp({ databaseUrl, port: await freePort() });
    try {
      for (let attempt = 0; attempt < 2; attempt += 1) {
        const started = Date.now();
        const response = await fetch(`${app.url}/healthz`);
        expect(response.status).toBe(503);
        expect(Date.now() - started).toBeLessThan(3000);
      }
    } finally {
      const started = Date.now();
      await app.stop();
      expect(Date.now() - started).toBeLessThan(10_000);
    }
  }, 30_000);
});

describe('stopping the app', () => {
  it('lets a request that is in progress finish', async () => {
    const databaseUrl = `postgres://127.0.0.1:${blackHole.address().port}/tapecraft`;
    const app = await startApp({ databaseUrl, port: await freePort() });
    const connected = new Promise((resolve) => blackHole.once('connection', resolve));
    const response = fetch(`${app.url}/healthz`);
    response.catch(() => {});
    await connected;
    const stopped = app.stop();
    expect((await response).status).toBe(503);
    expect(await stopped).toBe(0);
  }, 30_000);

  it('does not wait for a connection that never sends a request', async () => {
    const port = await freePort();
    const app = await startApp({ databaseUrl: 'postgres://127.0.0.1:1/tapecraft', port });
    const idle = net.connect(port, '127.0.0.1');
    idle.on('error', () => {});
    await new Promise((resolve) => idle.once('connect', resolve));
    const started = Date.now();
    const exitCode = await app.stop();
    idle.destroy();
    expect(exitCode).toBe(0);
    expect(Date.now() - started).toBeLessThan(7000);
  }, 30_000);
});

describe('npm run migrate without a reachable database', () => {
  const root = fileURLToPath(new URL('../..', import.meta.url));

  it('fails without printing the password', async () => {
    const databaseUrl = `postgres://tape:hunter2@127.0.0.1:${await freePort()}/tapecraft`;
    const error = await promisify(execFile)('npm', ['run', 'migrate'], {
      cwd: root,
      env: { ...process.env, DATABASE_URL: databaseUrl },
    }).then(
      () => undefined,
      (caught) => caught,
    );
    expect(error.code).toBe(1);
    expect(error.stderr).toContain('Migration failed');
    expect(error.stdout + error.stderr).not.toContain('hunter2');
  }, 30_000);
});
