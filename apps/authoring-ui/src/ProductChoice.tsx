import { useState } from 'react';
import type { Product, ProductChoiceValue } from './platform/client';

/**
 * Which product the label is about, chosen rather than typed (FN-AUT-008).
 *
 * @remarks
 * ADR-037 decision 3. The author picks a product and the surface writes its identity; there is
 * deliberately no box to type an identifier into, because that is transcription with a worse
 * error rate. The identity is never shown either - it is what the platform stores and resolves,
 * and it means nothing to the person choosing.
 */
export function ProductChoice({
  current,
  searchProducts,
  onChoose,
}: {
  readonly current: ProductChoiceValue | null;
  readonly searchProducts: (text: string) => Promise<readonly Product[]>;
  readonly onChoose: (product: ProductChoiceValue) => void;
}) {
  const [text, setText] = useState('');
  const [found, setFound] = useState<readonly Product[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const find = async () => {
    setProblem(null);

    try {
      setFound(await searchProducts(text.trim()));
    } catch (failed) {
      setFound(null);
      setProblem(failed instanceof Error ? failed.message : String(failed));
    }
  };

  return (
    <section>
      <h2>Product</h2>

      <p>
        {current === null ? (
          // Said plainly: a template instantiated before anybody chose one is normal, and an
          // empty space where a product should be reads as a defect rather than work
          // outstanding.
          <>This label is about no product yet.</>
        ) : (
          <>This label is about {current.display ?? 'a product with no recorded name'}.</>
        )}
      </p>

      <label>
        Find a product
        <input
          type="search"
          value={text}
          onChange={(event) => setText(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              void find();
            }
          }}
        />
      </label>
      <button type="button" onClick={() => void find()}>
        Find
      </button>

      {problem !== null && (
        <p role="alert">That search did not happen, so this is not an empty result: {problem}</p>
      )}

      {found !== null && found.length === 0 && (
        // Not "no such product". This surface knows nothing about what the system of record
        // holds beyond what it asked for.
        <p role="status">Nothing matched that.</p>
      )}

      {found !== null && found.length > 0 && (
        <ul>
          {found.map((product) => (
            <li key={product.identifier}>
              <button
                type="button"
                onClick={() =>
                  onChoose({ identifier: product.identifier, display: product.name })
                }
              >
                {product.name}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
