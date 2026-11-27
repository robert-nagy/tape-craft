import { fileURLToPath } from 'node:url';
import { runner } from 'node-pg-migrate';

const dir = fileURLToPath(new URL('../migrations', import.meta.url));

/**
 * Apply the pending migrations and return them. A second run started at the same
 * time waits for the first one to finish.
 */
export function runMigrations(databaseUrl, { logger = console } = {}) {
  return runner({
    databaseUrl: { connectionString: databaseUrl, connectionTimeoutMillis: 10_000 },
    dir,
    direction: 'up',
    migrationsTable: 'pgmigrations',
    advisoryLockMode: 'wait',
    logger,
  });
}
