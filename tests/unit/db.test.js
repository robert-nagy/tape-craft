import { describe, expect, it, vi } from 'vitest';
import { createPool } from '../../src/db.js';

describe('createPool', () => {
  it('limits how long a connection attempt may take', async () => {
    const pool = createPool('postgres://127.0.0.1:1/x', { error: vi.fn() });
    expect(pool.options.connectionTimeoutMillis).toBe(2000);
    await pool.end();
  });

  it('logs an idle connection error instead of crashing', async () => {
    const logger = { error: vi.fn() };
    const pool = createPool('postgres://127.0.0.1:1/x', logger);
    expect(() => pool.emit('error', new Error('terminating connection'))).not.toThrow();
    expect(logger.error).toHaveBeenCalledOnce();
    await pool.end();
  });
});
