import type { ReactNode } from 'react';

export function LandingPage({ children }: { children?: ReactNode }) {
  return (
    <main>
      <img src="/icons/icon-192.png" alt="" width={96} height={96} />
      <h1>tape-craft</h1>
      <p>Your Liked Songs, crafted into playlists that fit what you need.</p>
      {children}
    </main>
  );
}
