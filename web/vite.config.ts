import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  build: {
    // ASP.NET Core serves the built app from the same origin (ADR 0001).
    outDir: '../src/TapeCraft.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: { '/healthz': 'http://127.0.0.1:3000' },
  },
  test: {
    include: ['tests/**/*.test.{ts,tsx}'],
  },
});
