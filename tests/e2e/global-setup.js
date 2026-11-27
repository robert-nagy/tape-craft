import { execFileSync } from 'node:child_process';

// Build once, as a host would: the frontend into wwwroot, then the published backend.
export default function globalSetup() {
  execFileSync('npm', ['run', 'build', '--workspace', 'web'], { stdio: 'inherit' });
  execFileSync(
    'dotnet',
    ['publish', 'src/TapeCraft.Api', '-c', 'Release', '-o', '.artifacts/e2e', '-p:SkipFrontend=true'],
    { stdio: 'inherit' },
  );
}
