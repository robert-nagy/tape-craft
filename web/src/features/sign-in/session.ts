export type Session = { signedIn: false } | { signedIn: true; name: string };

const signedOut: Session = { signedIn: false };

/** Who is signed in, according to the server. Anything unexpected counts as signed out. */
export async function fetchSession(fetcher: typeof fetch = fetch): Promise<Session> {
  try {
    const response = await fetcher('/api/session', { cache: 'no-store', credentials: 'same-origin' });
    if (!response.ok) return signedOut;
    const body: unknown = await response.json();
    if (typeof body === 'object' && body !== null && 'signedIn' in body && body.signedIn === true) {
      const name = 'name' in body && typeof body.name === 'string' ? body.name : '';
      return { signedIn: true, name };
    }
    return signedOut;
  } catch {
    return signedOut;
  }
}
