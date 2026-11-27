import pg from 'pg';

export function createPool(databaseUrl, logger) {
  const pool = new pg.Pool({ connectionString: databaseUrl, connectionTimeoutMillis: 2000 });
  // Without a listener, losing an idle connection (a database restart) ends the process.
  pool.on('error', (error) => logger.error({ err: error.message }, 'idle database connection failed'));
  return pool;
}
