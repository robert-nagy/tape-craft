// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { LandingPage } from '../src/features/landing/LandingPage';

afterEach(cleanup);

describe('LandingPage', () => {
  it('names the app and says sign-in is not available yet', () => {
    render(<LandingPage />);
    expect(screen.getByRole('heading', { level: 1, name: 'tape-craft' })).toBeDefined();
    expect(screen.getByText(/not available yet/)).toBeDefined();
  });
});
