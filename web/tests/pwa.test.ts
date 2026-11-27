import { readFile } from 'node:fs/promises';
import { describe, expect, it } from 'vitest';

const read = (path: string) => readFile(new URL(`../${path}`, import.meta.url));

describe('web app manifest', () => {
  it('declares a name, standalone display and 192 and 512 px icons', async () => {
    const manifest = JSON.parse(String(await read('public/manifest.webmanifest')));
    expect(manifest).toMatchObject({
      name: 'tape-craft',
      short_name: 'tape-craft',
      id: '/',
      start_url: '/',
      scope: '/',
      display: 'standalone',
    });
    for (const size of [192, 512]) {
      expect(manifest.icons).toContainEqual(
        expect.objectContaining({ src: `/icons/icon-${size}.png`, sizes: `${size}x${size}`, type: 'image/png' }),
      );
    }
  });

  it.each([192, 512])('has a real %i px PNG icon', async (size) => {
    const png = await read(`public/icons/icon-${size}.png`);
    expect(png.subarray(1, 4).toString()).toBe('PNG');
    expect([png.readUInt32BE(16), png.readUInt32BE(20)]).toEqual([size, size]);
  });

  it('is linked from the page', async () => {
    expect(String(await read('index.html'))).toContain('<link rel="manifest" href="/manifest.webmanifest">');
  });

  it('has an offline page that says the server cannot be reached', async () => {
    expect(String(await read('public/offline.html'))).toContain('The server cannot be reached');
  });
});
