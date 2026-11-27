import { loadConfig } from './config.js';
import { runMigrations } from './migrations.js';

try {
  await runMigrations(loadConfig().databaseUrl);
} catch (error) {
  console.error(`Migration failed: ${error.message}`);
  process.exitCode = 1;
}
