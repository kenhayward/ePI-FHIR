import { useEffect, useState } from 'react';

/**
 * The leaflet a version produces, for an author to look at (FN-AUT-015).
 *
 * @remarks
 * <p>
 * The rendered HTML goes into a sandboxed frame and never into this page. It is the platform's
 * own output, and it is still a document assembled from content people type - putting it in this
 * page would give it this page's origin, its session and its access token. A sandboxed frame
 * gives it none of them, and a leaflet needs none of them.
 * </p>
 * <p>
 * It is a preview and says so. There is no template store yet, so nothing here is rendered with
 * a template anybody approved - and a render made with an unapproved template cannot be the
 * artefact filed with a regulator (ADR-033 decision 2, CAP-RND-004).
 * </p>
 */
export function LeafletPreview({ load }: { readonly load: () => Promise<string> }) {
  const [html, setHtml] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    let wanted = true;

    load()
      .then((rendered) => {
        if (wanted) {
          setHtml(rendered);
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
  }, [load]);

  return (
    <section>
      <h2>Preview</h2>
      <p>
        This is how the content reads as a leaflet. It is <strong>not the artefact</strong> that
        would be filed with a regulator: that is rendered with an approved template, and this is
        not.
      </p>

      {problem !== null && (
        <p role="alert">This preview could not be made, so it is not that there is nothing to show: {problem}</p>
      )}

      {html !== null && (
        // sandbox with no allowances at all: no scripts, no forms, no same-origin. A leaflet is
        // a document, and a document needs none of them.
        <iframe title="Preview of this version" sandbox="" srcDoc={html} />
      )}
    </section>
  );
}
