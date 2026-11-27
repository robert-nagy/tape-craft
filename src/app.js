import { STATUS_CODES } from 'node:http';
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
  const app = Fastify({
    logger,
    // Fastify answers these two cases without running hooks, so without the
    // security headers: requests that arrive while closing, and malformed URLs.
    return503OnClosing: false,
    frameworkErrors: (error, _request, reply) => {
      const status = error.statusCode ?? 400;
      reply.headers(securityHeaders).code(status).send({ error: STATUS_CODES[status] });
    },
  });

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
