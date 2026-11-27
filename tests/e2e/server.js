import { spawn } from 'node:child_process';
import net from 'node:net';
import { fileURLToPath } from 'node:url';

const published = fileURLToPath(new URL('../../.artifacts/e2e/', import.meta.url));

/** A TCP port that was free a moment ago. */
export function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

// The app runs as its own process group, so that stopping it also stops whatever `command` spawned.
/**
 * Start the app published by global-setup.js. Its Data Protection key ring goes to `keysDir`; the
 * Spotify app is a made-up one, so a test never reaches Spotify unless it routes the browser there.
 */
export async function startApp({ databaseUrl, port, keysDir, command = ['dotnet', `${published}TapeCraft.Api.dll`] }) {
  const [file, ...args] = command;
  const child = spawn(file, args, {
    cwd: published,
    detached: true,
    env: {
      ...process.env,
      DATABASE_URL: databaseUrl,
      PORT: String(port),
      HOST: '127.0.0.1',
      SPOTIFY_CLIENT_ID: 'e2eclientid0123',
      SPOTIFY_REDIRECT_URI: `http://127.0.0.1:${port}/auth/spotify/callback`,
      DATA_PROTECTION_KEYS_DIR: keysDir,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let output = '';
  child.stdout.on('data', (chunk) => (output += chunk));
  child.stderr.on('data', (chunk) => (output += chunk));
  // Resolves to the exit code, or null if a signal ended the process.
  const exited = new Promise((resolve) => child.once('exit', resolve));
  const url = `http://127.0.0.1:${port}`;

  async function stop(signal = 'SIGTERM') {
    if (child.exitCode === null && child.signalCode === null) process.kill(-child.pid, signal);
    return exited;
  }

  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) break;
    try {
      await fetch(url, { signal: AbortSignal.timeout(1000) });
      return { url, stop, output: () => output };
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  }
  await stop('SIGKILL');
  throw new Error(`The app did not start:\n${output}`);
}
