import { useEffect, useState } from 'react';
import type {
  FiledRender,
  OfficialRenderOutcome,
  RenderTemplateChoice,
} from './platform/client';

/**
 * The artefact of record, on a screen (FN-AUT-016).
 *
 * @remarks
 * <p>
 * The preview panel shows what content reads like and says it is not the filed artefact. This
 * one shows the filed artefact, and the distinction has to survive being on a screen: somebody
 * who cannot tell which of the two they are looking at is somebody who will eventually send the
 * wrong one (CAP-RND-004). Nothing here says "preview", and the frame is titled for what it is.
 * </p>
 * <p>
 * Producing one is a button rather than something that happens on open, because it files
 * something. A screen that filed an artefact as a side effect of being looked at would be
 * filing on somebody's behalf.
 * </p>
 * <p>
 * The HTML goes into a sandboxed frame and never into this page - the same rule as the preview,
 * for the same reason: it is the platform's own output and it is still a document assembled from
 * content people type, and this page's origin, session and token are none of its business.
 * </p>
 */
export function OfficialLeaflet({
  approvedTemplates,
  filedRenders,
  produce,
  artefact,
}: {
  readonly approvedTemplates: () => Promise<readonly RenderTemplateChoice[]>;
  readonly filedRenders: () => Promise<readonly FiledRender[]>;
  readonly produce: (template: string) => Promise<OfficialRenderOutcome>;
  readonly artefact: (template: string, templateVersion: number) => Promise<string>;
}) {
  const [templates, setTemplates] = useState<readonly RenderTemplateChoice[] | null>(null);
  const [chosen, setChosen] = useState<string>('');
  const [render, setRender] = useState<FiledRender | null>(null);
  const [html, setHtml] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let wanted = true;

    Promise.all([approvedTemplates(), filedRenders()])
      .then(([usable, already]) => {
        if (!wanted) {
          return;
        }

        setTemplates(usable);
        setChosen(usable[0]?.identifier ?? '');

        // What exists is offered rather than remade. An artefact already filed is the artefact,
        // and asking for it again would produce the same bytes at best.
        if (already.length > 0) {
          setRender(already[0]!);
        }
      })
      .catch((failed: Error) => {
        if (wanted) {
          setProblem(failed.message);
        }
      });

    return () => {
      wanted = false;
    };
  }, [approvedTemplates, filedRenders]);

  useEffect(() => {
    if (render === null) {
      return;
    }

    let wanted = true;

    artefact(render.template, render.templateVersion)
      .then((read) => {
        if (wanted) {
          setHtml(read);
        }
      })
      .catch((failed: Error) => {
        if (wanted) {
          setProblem(failed.message);
        }
      });

    return () => {
      wanted = false;
    };
  }, [artefact, render]);

  const askFor = async () => {
    setBusy(true);
    setProblem(null);

    try {
      const outcome = await produce(chosen);

      if (outcome.ok) {
        setRender(outcome.render);
        return;
      }

      // The platform's own words, because which rule refused is the only thing that says what to
      // do next: get the version approved, get a template approved, or look at a version that
      // exists.
      setProblem(
        outcome.kind === 'missing'
          ? 'There is no such version, so nothing was rendered.'
          : outcome.detail,
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <section>
      <h2>The filed leaflet</h2>
      <p>
        This is the artefact of record: rendered from an approved version with a template somebody
        approved, and filed where it can be cited.
      </p>

      {problem !== null && <p role="alert">{problem}</p>}

      {templates !== null && templates.length === 0 && (
        <p>
          There is <strong>no approved template</strong> in this deployment, so nothing can be
          rendered officially yet. A template owner has to take one through its approval first.
        </p>
      )}

      {templates !== null && templates.length > 0 && (
        <p>
          <label htmlFor="official-render-template">Template</label>{' '}
          <select
            id="official-render-template"
            value={chosen}
            onChange={(event) => setChosen(event.target.value)}
          >
            {templates.map((template) => (
              <option key={template.identifier} value={template.identifier}>
                {template.name}
              </option>
            ))}
          </select>{' '}
          <button type="button" onClick={() => void askFor()} disabled={busy || chosen === ''}>
            Produce the filed leaflet
          </button>
        </p>
      )}

      {render !== null && (
        <p>
          {render.alreadyFiled
            ? 'This was already filed, so nothing new was written.'
            : 'Filed.'}{' '}
          <code>{render.key}</code>
        </p>
      )}

      {html !== null && (
        // sandbox with no allowances at all: no scripts, no forms, no same-origin. A leaflet is
        // a document, and a document needs none of them.
        <iframe title="The filed artefact for this version" sandbox="" srcDoc={html} />
      )}
    </section>
  );
}
