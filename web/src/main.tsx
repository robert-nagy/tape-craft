import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { LandingPage } from './features/landing/LandingPage';
import { registerServiceWorker } from './registerServiceWorker';

const root = document.getElementById('root');
if (!root) throw new Error('The page has no #root element');

createRoot(root).render(
  <StrictMode>
    <LandingPage />
  </StrictMode>,
);

registerServiceWorker(navigator);
