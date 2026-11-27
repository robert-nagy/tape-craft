import { loadConfig } from './config.js';
import { describeError } from './errors.js';
import { runMigrations } from './migrations.js';

try {
  await runMigrations(loadConfig().databaseUrl);
} catch (error) {
  console.error(`Migration failed: ${describeError(error)}`);
  process.exitCode = 1;
}
