/**
 * Where the surface is pointed: the platform, and the identity provider that fronts it.
 */
export interface Settings {
  /** The OpenID Connect issuer, as the identity provider publishes it. */
  readonly authority: string;

  /** The public client this surface signs in as (ADR-039). */
  readonly clientId: string;

  /** The platform API's base address. */
  readonly api: string;
}

/** Where the configuration is served from, beside the bundle rather than from the platform. */
const file = 'config.json';

/**
 * Reads where the surface is pointed, when it starts (FN-AUT-017, ADR-049).
 *
 * @remarks
 * <p>
 * At start-up rather than at build time. A static bundle usually bakes its addresses in, and that
 * is wrong here for the reason ADR-012 gives about the service tier: the artefact that was tested
 * must be the artefact that ships, and an image with a hostname compiled into it cannot be
 * promoted between environments - only rebuilt for each, which means what runs in production is
 * not what CI proved.
 * </p>
 * <p>
 * Beside the bundle, not from the platform. Fetching it from the API would need the API's address
 * in order to read the file that says where the API is.
 * </p>
 * <p>
 * Everything here refuses. A surface that defaulted to localhost would fail in a deployment in a
 * way nobody would attribute to configuration - the defect class the service side has been bitten
 * by three times, and the reason tools/verify-configuration-paths.py exists.
 * </p>
 */
function isBrowsable(given: string): boolean {
  if (!URL.canParse(given)) {
    return false;
  }

  const protocol = new URL(given).protocol;
  return protocol === 'http:' || protocol === 'https:';
}

export async function loadSettings(
  fetcher: typeof fetch = globalThis.fetch.bind(globalThis),
): Promise<Settings> {
  const response = await fetcher(file, { cache: 'no-store' });

  if (!response.ok) {
    throw new Error(
      `${file} answered ${response.status}, so this surface does not know where the platform is. ` +
        'It is refusing to start rather than guessing.',
    );
  }

  let parsed: unknown;

  try {
    parsed = await response.json();
  } catch {
    throw new Error(
      `${file} could not be read as JSON, so this surface does not know where the platform is.`,
    );
  }

  const read = (parsed ?? {}) as Partial<Record<keyof Settings, unknown>>;

  // Every problem at once. Naming only the first means one restart per missing field, and an
  // operator learning the shape of the file by being refused four times.
  const problems: string[] = [];
  const value = (name: keyof Settings, mustBeAnAddress: boolean): string => {
    const given = read[name];

    if (typeof given !== 'string' || given === '') {
      problems.push(`'${name}' is missing.`);
      return '';
    }

    // Caught here rather than as an opaque failure on the first request. A host and port with no
    // scheme is the mistake somebody will actually make - and URL.canParse alone does not catch
    // it, because 'localhost:8080' parses with 'localhost:' as its scheme. The test for this
    // passed against canParse, which is why the protocol is checked as well.
    if (mustBeAnAddress && !isBrowsable(given)) {
      problems.push(`'${name}' is '${given}', which is not an address a browser can use.`);
      return '';
    }

    return given;
  };

  const settings: Settings = {
    authority: value('authority', true),
    clientId: value('clientId', false),
    api: value('api', true),
  };

  if (problems.length > 0) {
    throw new Error(
      `${file} does not say where this surface should look: ${problems.join(' ')} ` +
        'It is refusing to start rather than guessing.',
    );
  }

  return settings;
}
