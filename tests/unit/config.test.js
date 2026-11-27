import { describe, expect, it } from 'vitest';
import { loadConfig } from '../../src/config.js';

const url = 'postgres://localhost:5432/tapecraft';

describe('loadConfig', () => {
  it('reads the database URL and applies defaults', () => {
    expect(loadConfig({ DATABASE_URL: url })).toEqual({ databaseUrl: url, port: 3000, host: '127.0.0.1' });
  });

  it('reads PORT and HOST', () => {
    const config = loadConfig({ DATABASE_URL: url, PORT: '8080', HOST: '0.0.0.0' });
    expect(config).toMatchObject({ port: 8080, host: '0.0.0.0' });
  });

  // Fastify opens a second server for localhost, which shutdown would not wait for.
  it('listens on one address when HOST is localhost', () => {
    expect(loadConfig({ DATABASE_URL: url, HOST: 'localhost' }).host).toBe('127.0.0.1');
  });

  it('accepts the postgresql:// scheme', () => {
    expect(loadConfig({ DATABASE_URL: 'postgresql://localhost/x' }).databaseUrl).toBe('postgresql://localhost/x');
  });

  it.each([undefined, '', '   '])('requires DATABASE_URL (%j)', (value) => {
    expect(() => loadConfig({ DATABASE_URL: value })).toThrow(/DATABASE_URL is required/);
  });

  it.each(['mysql://user:hunter2@localhost/x', 'user:hunter2@localhost'])(
    'rejects a non-PostgreSQL URL without echoing it',
    (value) => {
      let error;
      try {
        loadConfig({ DATABASE_URL: value });
      } catch (caught) {
        error = caught;
      }
      expect(error.message).toMatch(/DATABASE_URL must start with postgres/);
      expect(error.message).not.toContain('hunter2');
    },
  );

  it.each(['abc', '-1', '65536', '3000.5', '0x50', ' '])('rejects PORT %j', (value) => {
    expect(() => loadConfig({ DATABASE_URL: url, PORT: value })).toThrow(/PORT must be/);
  });
});
