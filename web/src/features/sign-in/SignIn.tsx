import { useEffect, useState } from 'react';
import { fetchSession, type Session } from './session';

// Fixed messages only: the page never shows text taken from the address bar.
const messages: Record<string, string> = {
  denied: 'Sign-in was cancelled on Spotify. You can try again whenever you like.',
  failed: 'We could not sign you in with Spotify. Please try again.',
};

type Props = {
  loadSession?: () => Promise<Session>;
  search?: string;
};

export function SignIn({ loadSession = fetchSession, search = window.location.search }: Props) {
  const [session, setSession] = useState<Session | null>(null);

  // Asked once, when the page opens; sign-in and its errors arrive as a full page load.
  useEffect(() => {
    let current = true;
    loadSession().then((answer) => current && setSession(answer));
    return () => {
      current = false;
    };
  }, []);

  const code = new URLSearchParams(search).get('signin');
  const message = code !== null && Object.hasOwn(messages, code) ? messages[code] : null;

  return (
    <section aria-label="Spotify account">
      {message && !session?.signedIn && <p role="alert">{message}</p>}
      {session?.signedIn === true && <p>Signed in as {session.name}</p>}
      {session?.signedIn === false && (
        <a className="button" href="/auth/spotify/sign-in">
          Sign in with Spotify
        </a>
      )}
    </section>
  );
}
