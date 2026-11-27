import { describe, expect, it, vi } from 'vitest';
import { registerServiceWorker } from '../src/registerServiceWorker';

describe('registerServiceWorker', () => {
  it('registers /sw.js', () => {
    const register = vi.fn().mockResolvedValue({});
    registerServiceWorker({ serviceWorker: { register } } as unknown as Navigator);
    expect(register).toHaveBeenCalledWith('/sw.js');
  });

  it('warns instead of failing when registration is rejected', async () => {
    const warn = vi.fn();
    const register = vi.fn().mockRejectedValue(new Error('insecure origin'));
    registerServiceWorker({ serviceWorker: { register } } as unknown as Navigator, { warn });
    await vi.waitFor(() => expect(warn).toHaveBeenCalledOnce());
  });

  it('does nothing in a browser without service workers', () => {
    expect(() => registerServiceWorker({} as Navigator)).not.toThrow();
  });
});
