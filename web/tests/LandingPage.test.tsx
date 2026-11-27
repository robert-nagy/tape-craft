// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { LandingPage } from '../src/features/landing/LandingPage';

afterEach(cleanup);

describe('LandingPage', () => {
  it('names the app and shows what it is given', () => {
    render(
      <LandingPage>
        <p>sign-in goes here</p>
      </LandingPage>,
    );
    expect(screen.getByRole('heading', { level: 1, name: 'tape-craft' })).toBeDefined();
    expect(screen.getByText('sign-in goes here')).toBeDefined();
    expect(screen.queryByText(/not available yet/)).toBeNull();
  });
});
