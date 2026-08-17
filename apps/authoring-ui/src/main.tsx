import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { PlatformClient } from './platform/client';
import { loadSettings } from './platform/settings';
import { SignIn } from './platform/signIn';

/**
 * Starts the application once it knows where it is pointed (ADR-049).
 *
 * @remarks
 * <p>
 * The addresses are read from <c>config.json</c> served beside the bundle, not from the
 * environment it was built in. This used to say the opposite - "read at build time, as a static
 * bundle must" - and that was wrong twice over: a static bundle can perfectly well fetch a file
 * before it renders, and baking an address in means the artefact CI proved is not the artefact
 * that ships, because each environment gets its own build.
 * </p>
 * <p>
 * Nothing renders until the configuration is read. A surface that started and then discovered it
 * had nowhere to talk to would show an author an empty screen and no reason for it.
 * </p>
 */
async function start(): Promise<void> {
  const settings = await loadSettings();

  const signIn = new SignIn({
    authority: settings.authority,
    clientId: settings.clientId,
    redirectUri: `${window.location.origin}/`,
  });

  const platform = new PlatformClient({
    baseUrl: settings.api,
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
}

start().catch((failed: Error) => {
  // On the page rather than only in the console. A blank surface with an explanation nobody sees
  // is a surface people report as "it does not work", and the answer is almost always one line of
  // configuration.
  const root = document.getElementById('root');

  if (root !== null) {
    const problem = document.createElement('p');
    problem.setAttribute('role', 'alert');
    problem.textContent = failed.message;
    root.replaceChildren(problem);
  }

  throw failed;
});
