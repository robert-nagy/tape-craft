import { buildApp } from './app.js';
import { loadConfig } from './config.js';
import { createPool } from './db.js';
import { createHealthCheck } from './health.js';

let config;
try {
  config = loadConfig();
} catch (error) {
  console.error(error.message);
  process.exit(1);
}

let app;
// The pool and the health check log through the app's logger, which exists once the app does.
const logger = {
  warn: (...args) => app.log.warn(...args),
  error: (...args) => app.log.error(...args),
};
const pool = createPool(config.databaseUrl, logger);
app = buildApp({ checkDatabase: createHealthCheck(pool, { logger }), logger: true });

let stopping = false;
async function stop(signal) {
  if (stopping) return;
  stopping = true;
  app.log.info({ signal }, 'stopping');
  // Requests in progress get a few seconds to finish. Whatever is still open
  // then, including connections that never sent a request, is cut off.
  setTimeout(() => app.server.closeAllConnections(), 5000).unref();
  // A hung database connection must not keep the process alive.
  setTimeout(() => process.exit(1), 8000).unref();
  await app.close();
  await pool.end();
  process.exit(0);
}
process.on('SIGTERM', stop);
process.on('SIGINT', stop);

try {
  await app.listen({ port: config.port, host: config.host });
} catch (error) {
  app.log.error(error);
  process.exit(1);
}
