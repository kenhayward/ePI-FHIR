import { describe, expect, it, vi } from 'vitest';
import { loadSettings } from '../src/platform/settings';

// Where the surface is pointed, read when it starts (FN-AUT-017).
//   CAP-CFG-006 Resolve every configuration a component needs at start-up
//
// It used to be read at build time, from Vite's environment. That is the usual shape for a static
// bundle and it is wrong here for the reason ADR-012 gives about the service tier: the artefact
// that was tested must be the artefact that ships, and an image with a hostname baked into it
// cannot be promoted between environments - it can only be rebuilt for each, which means what
// runs in production is not what CI proved (ADR-049).
//
// Everything here is about refusing. A surface that defaulted to localhost would fail in a
// deployment in a way nobody would attribute to configuration, which is the defect class this
// repository has been bitten by three times on the service side.
describe('FN-AUT-017 where the surface is pointed', () => {
  const settings = {
    authority: 'http://localhost:8081/realms/epi',
    clientId: 'epi-authoring-ui',
    api: 'http://localhost:8080',
  };

  const serving = (status: number, body: unknown) =>
    vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) =>
      new Response(typeof body === 'string' ? body : JSON.stringify(body), {
        status,
        headers: { 'content-type': 'application/json' },
      }),
    );

  it('reads where the platform and the identity provider are', async () => {
    const read = await loadSettings(serving(200, settings) as unknown as typeof fetch);

    expect(read.api).toBe('http://localhost:8080');
    expect(read.authority).toBe('http://localhost:8081/realms/epi');
    expect(read.clientId).toBe('epi-authoring-ui');
  });

  it('asks for the configuration beside the application, not from the platform', async () => {
    // Served with the bundle, so a surface that reached the browser has its configuration by
    // construction. Fetching it from the API would need the API's address to read the file that
    // says where the API is.
    const fetcher = serving(200, settings);
    await loadSettings(fetcher as unknown as typeof fetch);

    expect(String(fetcher.mock.calls[0]![0])).toContain('config.json');
  });

  it('refuses a configuration that is not there rather than guessing', async () => {
    await expect(loadSettings(serving(404, {}) as unknown as typeof fetch))
      .rejects.toThrow(/config\.json/);
  });

  it('refuses a configuration it cannot parse', async () => {
    await expect(loadSettings(serving(200, 'not json at all') as unknown as typeof fetch))
      .rejects.toThrow(/could not be read/i);
  });

  it('names every field that is missing, not just the first', async () => {
    // So one restart fixes the configuration rather than three.
    await expect(loadSettings(serving(200, { clientId: 'epi-authoring-ui' }) as unknown as typeof fetch))
      .rejects.toThrow(/authority[\s\S]*api|api[\s\S]*authority/);
  });

  it('treats an empty string as missing', async () => {
    // A field somebody cleared rather than removed. Both mean the same thing and neither is a
    // value to point a browser at.
    await expect(loadSettings(serving(200, { ...settings, api: '' }) as unknown as typeof fetch))
      .rejects.toThrow(/api/);
  });

  it('refuses an address that is not one', async () => {
    // Caught here rather than as an opaque failure on the first request. "localhost:8080" without
    // a scheme is the mistake somebody will actually make.
    await expect(
      loadSettings(serving(200, { ...settings, api: 'localhost:8080' }) as unknown as typeof fetch),
    ).rejects.toThrow(/localhost:8080/);
  });

  it('says which file it was reading when it refused', async () => {
    // An operator has to know what to edit. "Configuration is invalid" sends them looking.
    await expect(loadSettings(serving(200, {}) as unknown as typeof fetch))
      .rejects.toThrow(/config\.json/);
  });
});
