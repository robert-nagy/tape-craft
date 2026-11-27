import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { PostgreSqlContainer } from '@testcontainers/postgresql';
import pg from 'pg';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { buildApp } from '../../src/app.js';
import { createPool } from '../../src/db.js';
import { createHealthCheck } from '../../src/health.js';
import { runMigrations } from '../../src/migrations.js';
import { freePort, startApp } from '../helpers/server.js';

const TABLES = ['analysis', 'playlists', 'tokens', 'tracks', 'users'];
const silent = { warn() {}, error() {}, info() {}, debug() {} };

let container;
let databaseUrl;

async function query(text) {
  const client = new pg.Client({ connectionString: databaseUrl });
  await client.connect();
  try {
    return (await client.query(text)).rows;
  } finally {
    await client.end();
  }
}

const publicTables = async () =>
  (await query("SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename")).map(
    (row) => row.tablename,
  );

beforeAll(async () => {
  container = await new PostgreSqlContainer('postgres:17-alpine').start();
  databaseUrl = container.getConnectionUri();
}, 180_000);

afterAll(async () => {
  await container?.stop();
}, 60_000);

describe('migrations', () => {
  it('create the five tables in an empty database', async () => {
    expect(await publicTables()).toEqual([]);
    const applied = await runMigrations(databaseUrl, { logger: silent });
    expect(applied).not.toEqual([]);
    expect(await publicTables()).toEqual(expect.arrayContaining(TABLES));
  });

  it('change nothing and report no error when run again', async () => {
    const snapshot = async () => ({
      tables: await publicTables(),
      history: await query('SELECT * FROM pgmigrations ORDER BY id'),
    });
    const before = await snapshot();
    await expect(runMigrations(databaseUrl, { logger: silent })).resolves.toEqual([]);
    expect(await snapshot()).toEqual(before);
  });

  it('run through npm run migrate', async () => {
    const { stdout } = await promisify(execFile)('npm', ['run', 'migrate'], {
      env: { ...process.env, DATABASE_URL: databaseUrl },
    });
    expect(stdout).toContain('No migrations to run');
  }, 30_000);
});

describe('the running app', () => {
  it('serves the landing page and reports healthy when started with npm start', async () => {
    const app = await startApp({ databaseUrl, port: await freePort(), command: ['npm', 'start'] });
    try {
      expect((await fetch(`${app.url}/`)).status).toBe(200);
      expect((await fetch(`${app.url}/healthz`)).status).toBe(200);
    } finally {
      await app.stop();
    }
  }, 30_000);

  // Last in this file: it stops the database.
  it('answers 200, then 503 within 3 seconds once the database stops, and stays up', async () => {
    const pool = createPool(databaseUrl, silent);
    const app = buildApp({ checkDatabase: createHealthCheck(pool, { logger: silent }) });
    try {
      expect((await app.inject({ url: '/healthz' })).statusCode).toBe(200);
      await container.stop();
      container = undefined;
      const started = Date.now();
      expect((await app.inject({ url: '/healthz' })).statusCode).toBe(503);
      expect(Date.now() - started).toBeLessThan(3000);
      expect((await app.inject({ url: '/' })).statusCode).toBe(200);
    } finally {
      await app.close();
      await pool.end();
    }
  }, 60_000);
});
