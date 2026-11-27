const QUERY_TIMEOUT_MS = 2000;
const DEADLINE_MS = 2500;

/**
 * Return a function that resolves to whether the database answers. It resolves
 * within the deadline even if the connection hangs, and never rejects.
 * Concurrent calls share one query, so a public health endpoint cannot use up
 * the connection pool.
 */
export function createHealthCheck(pool, { logger }) {
  let pending;

  async function check() {
    let timer;
    const deadline = new Promise((_resolve, reject) => {
      timer = setTimeout(() => reject(new Error('database check timed out')), DEADLINE_MS);
    });
    try {
      await Promise.race([pool.query({ text: 'SELECT 1', query_timeout: QUERY_TIMEOUT_MS }), deadline]);
      return true;
    } catch (error) {
      logger.warn({ err: error.message }, 'database check failed');
      return false;
    } finally {
      clearTimeout(timer);
    }
  }

  return () => {
    pending ??= check().finally(() => {
      pending = undefined;
    });
    return pending;
  };
}
