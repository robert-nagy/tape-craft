// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SignIn } from '../src/features/sign-in/SignIn';
import { fetchSession } from '../src/features/sign-in/session';

afterEach(cleanup);

const answer = (body: unknown, status = 200) =>
  vi.fn(async () => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } }));

describe('fetchSession', () => {
  it('asks the server who is signed in, without caching', async () => {
    const fetcher = answer({ signedIn: true, name: 'Ada' });
    expect(await fetchSession(fetcher)).toEqual({ signedIn: true, name: 'Ada' });
    expect(fetcher).toHaveBeenCalledWith('/api/session', { cache: 'no-store', credentials: 'same-origin' });
  });

  it('treats a failed or odd answer as signed out', async () => {
    expect(await fetchSession(answer({ signedIn: true, name: 'Ada' }, 500))).toEqual({ signedIn: false });
    expect(await fetchSession(answer({ signedIn: 'yes' }))).toEqual({ signedIn: false });
    expect(await fetchSession(vi.fn(async () => Promise.reject(new TypeError('offline'))))).toEqual({ signedIn: false });
  });
});

describe('SignIn', () => {
  it('offers to sign in with Spotify when signed out', async () => {
    render(<SignIn loadSession={async () => ({ signedIn: false })} search="" />);
    const link = await screen.findByRole('link', { name: 'Sign in with Spotify' });
    expect(link.getAttribute('href')).toBe('/auth/spotify/sign-in');
  });

  it('shows who is signed in', async () => {
    render(<SignIn loadSession={async () => ({ signedIn: true, name: 'Ada' })} search="" />);
    expect(await screen.findByText('Signed in as Ada')).toBeDefined();
    expect(screen.queryByRole('link', { name: 'Sign in with Spotify' })).toBeNull();
  });

  it('explains a sign-in the listener cancelled on Spotify', async () => {
    render(<SignIn loadSession={async () => ({ signedIn: false })} search="?signin=denied" />);
    expect((await screen.findByRole('alert')).textContent).toMatch(/cancelled/);
    expect(await screen.findByRole('link', { name: 'Sign in with Spotify' })).toBeDefined();
  });

  it('explains a sign-in that failed', async () => {
    render(<SignIn loadSession={async () => ({ signedIn: false })} search="?signin=failed" />);
    expect((await screen.findByRole('alert')).textContent).toMatch(/could not sign you in/);
  });

  it('never shows text from the address bar', async () => {
    render(<SignIn loadSession={async () => ({ signedIn: false })} search="?signin=%3Cb%3Eyou%20were%20hacked%3C%2Fb%3E" />);
    await screen.findByRole('link', { name: 'Sign in with Spotify' });
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.queryByText(/hacked/)).toBeNull();
  });
});
