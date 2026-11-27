/** Registers the service worker that shows the offline page (public/sw.js). */
export function registerServiceWorker(nav: Navigator, log: Pick<Console, 'warn'> = console): void {
  if (!('serviceWorker' in nav)) return;
  nav.serviceWorker.register('/sw.js').catch((error: unknown) => {
    log.warn('The offline page is not available:', error);
  });
}
