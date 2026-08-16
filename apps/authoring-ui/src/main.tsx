import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { PlatformClient } from './platform/client';
import { SignIn } from './platform/signIn';

/**
 * Where the application is pointed, from the environment it was built for.
 *
 * @remarks
 * Read at build time, as a static bundle must - there is no server here to read configuration on
 * start-up. A deployment against a different identity provider or API is a different build,
 * which is what ADR-014's container-per-environment shape already implies for the web tier.
 *
 * Refused rather than defaulted when absent. A surface silently pointed at localhost in a
 * deployment would fail in a way nobody would attribute to configuration, which is the defect
 * class tools/verify-configuration-paths.py exists for on the service side.
 */
const required = (name: string): string => {
  const value = import.meta.env[name] as string | undefined;

  if (value === undefined || value === '') {
    throw new Error(
      `${name} was not set when this application was built, so it does not know where to find ` +
        'the platform. It is refusing to start rather than guessing.',
    );
  }

  return value;
};

const signIn = new SignIn({
  authority: required('VITE_EPI_AUTHORITY'),
  clientId: required('VITE_EPI_CLIENT_ID'),
  redirectUri: `${window.location.origin}/`,
});

const platform = new PlatformClient({
  baseUrl: required('VITE_EPI_API'),
  token: () => signIn.tokenAsync(),
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App
      session={signIn}
      platform={platform}
      location={new URL(window.location.href)}
      go={(url) => window.location.assign(url)}
    />
  </StrictMode>,
);
