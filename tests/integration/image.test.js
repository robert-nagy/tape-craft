import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { PostgreSqlContainer } from '@testcontainers/postgresql';
import { GenericContainer, Network, Wait } from 'testcontainers';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

const root = fileURLToPath(new URL('../..', import.meta.url));
// A new tag for every run: a failed build then cannot fall back on an older image.
const IMAGE = `localhost/tape-craft:test-${randomUUID()}`;
const started = [];
let network;
let environment;

beforeAll(async () => {
  network = await new Network().start();
  const database = await new PostgreSqlContainer('postgres:17-alpine')
    .withNetwork(network)
    .withNetworkAliases('database')
    .start();
  started.push(database);
  environment = {
    DATABASE_URL: `postgres://${database.getUsername()}:${database.getPassword()}@database:5432/${database.getDatabase()}`,
  };
  await GenericContainer.fromDockerfile(root).build(IMAGE);
}, 600_000);

afterAll(async () => {
  for (const container of started.reverse()) await container.stop();
  await network?.stop();
}, 60_000);

describe('container image', () => {
  it('runs the migrations and serves the app, configured by environment variables only', async () => {
    const migrate = await new GenericContainer(IMAGE)
      .withNetwork(network)
      .withEnvironment(environment)
      .withCommand(['node', 'src/migrate.js'])
      .withWaitStrategy(Wait.forOneShotStartup())
      .start();
    started.push(migrate);

    const app = await new GenericContainer(IMAGE)
      .withNetwork(network)
      .withEnvironment(environment)
      .withExposedPorts(3000)
      .withWaitStrategy(Wait.forHttp('/healthz', 3000))
      .start();
    started.push(app);

    const url = `http://${app.getHost()}:${app.getMappedPort(3000)}`;
    expect((await fetch(`${url}/`)).status).toBe(200);
    expect((await fetch(`${url}/healthz`)).status).toBe(200);
  }, 120_000);
});
