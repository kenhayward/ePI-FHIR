import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ProductChoice } from '../src/ProductChoice';

// Choosing the product a label is about (FN-AUT-008).
//   CAP-MDM-008 Expose an identifier resolution and association API
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//
// ADR-037 decision 3: no identifier is ever typed. An author picks a product and the surface
// writes its identity - which is the whole reason ADR-040 made content able to carry one.
describe('FN-AUT-008 choosing a product', () => {
  const products = [
    { identifier: 'PROD-0001', name: 'SYNTHETIC - Examplinum 10 mg tablets', markets: ['GB'] },
    { identifier: 'PROD-0002', name: 'SYNTHETIC - Examplinum 20 mg tablets', markets: ['GB'] },
  ];

  const search = () => vi.fn(async () => products);

  it('says what the label is about now', async () => {
    render(
      <ProductChoice
        current={{ identifier: 'PROD-0001', display: 'SYNTHETIC - Examplinum 10 mg tablets' }}
        searchProducts={search()}
        onChoose={vi.fn()}
      />,
    );

    expect(screen.getByText(/SYNTHETIC - Examplinum 10 mg tablets/)).toBeDefined();
  });

  it('says plainly when the label is about no product yet', async () => {
    // A template instantiated before anybody chose one is normal, and an empty space where a
    // product should be reads as a defect rather than as work outstanding.
    render(<ProductChoice current={null} searchProducts={search()} onChoose={vi.fn()} />);

    expect(screen.getByText(/no product/i)).toBeDefined();
  });

  it('never shows the author an identifier to type', async () => {
    // ADR-037 decision 3, asserted rather than assumed. The identity is what the platform
    // writes; a box for it would be transcription with a worse error rate.
    const { container } = render(
      <ProductChoice
        current={{ identifier: 'PROD-0001', display: 'SYNTHETIC - Examplinum 10 mg tablets' }}
        searchProducts={search()}
        onChoose={vi.fn()}
      />,
    );

    expect(container.textContent).not.toContain('PROD-0001');
  });

  it('offers products matching what the author searched for', async () => {
    const searchProducts = search();
    render(<ProductChoice current={null} searchProducts={searchProducts} onChoose={vi.fn()} />);

    await userEvent.type(screen.getByLabelText(/find a product/i), 'examplinum');
    await userEvent.click(screen.getByRole('button', { name: /^find$/i }));

    expect(searchProducts).toHaveBeenCalledWith('examplinum');
    expect(await screen.findByRole('button', { name: /10 mg tablets/ })).toBeDefined();
  });

  it('reports the chosen product by identity, and the name only for a reader', async () => {
    const onChoose = vi.fn();
    render(<ProductChoice current={null} searchProducts={search()} onChoose={onChoose} />);

    await userEvent.type(screen.getByLabelText(/find a product/i), 'examplinum');
    await userEvent.click(screen.getByRole('button', { name: /^find$/i }));
    await userEvent.click(await screen.findByRole('button', { name: /10 mg tablets/ }));

    expect(onChoose).toHaveBeenCalledWith({
      identifier: 'PROD-0001',
      display: 'SYNTHETIC - Examplinum 10 mg tablets',
    });
  });

  it('says a search failed rather than showing it as no products', async () => {
    const failing = vi.fn(async () => {
      throw new Error('The platform answered 503.');
    });
    render(<ProductChoice current={null} searchProducts={failing} onChoose={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /^find$/i }));

    expect(await screen.findByRole('alert')).toBeDefined();
  });

  it('says nothing matched, without saying no such product exists', async () => {
    // The directory answers over whatever the system of record is, and this surface knows
    // nothing about what it holds beyond what it was asked for.
    render(
      <ProductChoice current={null} searchProducts={vi.fn(async () => [])} onChoose={vi.fn()} />,
    );

    await userEvent.click(screen.getByRole('button', { name: /^find$/i }));

    expect(await screen.findByText(/nothing matched/i)).toBeDefined();
  });
});
