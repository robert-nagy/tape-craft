/**
 * Read the app's configuration from environment variables. Error messages never
 * include a value, because DATABASE_URL carries a password.
 */
export function loadConfig(env = process.env) {
  const databaseUrl = env.DATABASE_URL?.trim();
  if (!databaseUrl) throw new Error('DATABASE_URL is required');
  if (!/^postgres(ql)?:\/\//.test(databaseUrl)) {
    throw new Error('DATABASE_URL must start with postgres:// or postgresql://');
  }

  const port = env.PORT === undefined || env.PORT === '' ? '3000' : env.PORT;
  if (!/^\d+$/.test(port) || Number(port) > 65535) {
    throw new Error('PORT must be a whole number from 0 to 65535');
  }

  // For localhost Fastify opens one server per address, and shutdown only waits for the first.
  const host = !env.HOST || env.HOST === 'localhost' ? '127.0.0.1' : env.HOST;

  return { databaseUrl, port: Number(port), host };
}
