import { afterEach, describe, expect, it, vi } from 'vitest';
import { createHealthCheck } from '../../src/health.js';

const logger = () => ({ warn: vi.fn() });

afterEach(() => vi.useRealTimers());

describe('createHealthCheck', () => {
  it('is healthy when the query succeeds', async () => {
    const pool = { query: vi.fn().mockResolvedValue({ rows: [] }) };
    await expect(createHealthCheck(pool, { logger: logger() })()).resolves.toBe(true);
    expect(pool.query).toHaveBeenCalledWith(expect.objectContaining({ text: 'SELECT 1', query_timeout: 2000 }));
  });

  it('is unhealthy and logs the cause when the query fails', async () => {
    const log = logger();
    const pool = { query: vi.fn().mockRejectedValue(new Error('connect ECONNREFUSED')) };
    await expect(createHealthCheck(pool, { logger: log })()).resolves.toBe(false);
    expect(log.warn).toHaveBeenCalledOnce();
  });

  it('logs a cause even when the error has no message', async () => {
    const log = logger();
    const error = new AggregateError([new Error('connect ECONNREFUSED ::1:5432')], '');
    await createHealthCheck({ query: vi.fn().mockRejectedValue(error) }, { logger: log })();
    expect(log.warn).toHaveBeenCalledWith({ err: 'connect ECONNREFUSED ::1:5432' }, 'database check failed');
  });

  it('is unhealthy when the query throws synchronously', async () => {
    const pool = {
      query: () => {
        throw new Error('pool is closed');
      },
    };
    await expect(createHealthCheck(pool, { logger: logger() })()).resolves.toBe(false);
  });

  it('gives up after the deadline when the query never settles', async () => {
    vi.useFakeTimers();
    const pool = { query: () => new Promise(() => {}) };
    let result;
    createHealthCheck(pool, { logger: logger() })().then((value) => (result = value));
    await vi.advanceTimersByTimeAsync(2499);
    expect(result).toBeUndefined();
    await vi.advanceTimersByTimeAsync(1);
    expect(result).toBe(false);
  });

  it('shares one query between concurrent calls, then queries again', async () => {
    let finish;
    const pool = { query: vi.fn(() => new Promise((resolve) => (finish = resolve))) };
    const check = createHealthCheck(pool, { logger: logger() });
    const both = Promise.all([check(), check()]);
    await Promise.resolve();
    expect(pool.query).toHaveBeenCalledTimes(1);
    finish({});
    await expect(both).resolves.toEqual([true, true]);
    const next = check();
    await Promise.resolve();
    expect(pool.query).toHaveBeenCalledTimes(2);
    finish({});
    await next;
  });
});
