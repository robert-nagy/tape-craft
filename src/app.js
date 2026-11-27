import { fileURLToPath } from 'node:url';
import fastifyStatic from '@fastify/static';
import Fastify from 'fastify';

const publicDir = fileURLToPath(new URL('../public', import.meta.url));

const securityHeaders = {
  'content-security-policy':
    "default-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'",
  'x-content-type-options': 'nosniff',
  'referrer-policy': 'no-referrer',
};

/**
 * @param {object} options
 * @param {() => Promise<boolean>} options.checkDatabase whether the database answers
 * @param {import('fastify').FastifyServerOptions['logger']} [options.logger]
 */
export function buildApp({ checkDatabase, logger = false }) {
  const app = Fastify({ logger });

  app.addHook('onRequest', async (_request, reply) => {
    reply.headers(securityHeaders);
  });

  app.register(fastifyStatic, { root: publicDir });

  app.get('/healthz', async (_request, reply) => {
    const healthy = await checkDatabase();
    reply.code(healthy ? 200 : 503).header('cache-control', 'no-store');
    return { status: healthy ? 'ok' : 'unavailable' };
  });

  return app;
}
