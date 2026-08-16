import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MarketApprovals } from '../src/MarketApprovals';

// Where each market stands, and what may be done about it (FN-AUT-011).
//   CAP-LCM-003 Per-market regulatory-approval state held separately from internal state
//   CAP-LCM-012 A signature to submit to a regulator; none to record its decision
//   CAP-LCM-004 Effective dating
//
// Two rules the platform holds carefully, and this screen is where they would blur. Submitting
// is an act of this organisation by an accountable person and is signed; recording what a
// regulator decided is a factual entry about somebody else's decision and is not. And only the
// transition that records an approval may say when it takes effect.
describe('FN-AUT-011 where each market stands', () => {
  const markets = {
    GB: {
      state: 'not-submitted',
      actions: ['submit'],
      signedActions: ['submit'],
      actionsNeedingEffectiveDate: [],
      signatureMeanings: { submit: 'responsibility' },
    },
    DE: {
      state: 'under-assessment',
      actions: ['record-approval', 'record-rejection'],
      signedActions: [],
      actionsNeedingEffectiveDate: ['record-approval'],
      signatureMeanings: {},
    },
  };

  const platform = () => ({
    marketTransition: vi.fn(async () => ({ ok: true as const, from: 'x', to: 'y' })),
    sign: vi.fn(async () => ({ refused: false as const, reference: 'sig-1' })),
  });

  const subject = { documentIdentifier: 'doc-1', version: 2 };

  it('names every market separately, so approved never reads as approved everywhere', async () => {
    // ADR-005. A version approved in one market and not another is the ordinary case, and a
    // screen that summarised it would be summarising away the whole point.
    render(
      <MarketApprovals version={subject} markets={markets} {...platform()} onDone={vi.fn()} />,
    );

    expect(screen.getByText(/GB/)).toBeDefined();
    expect(screen.getByText(/not-submitted/)).toBeDefined();
    expect(screen.getByText(/DE/)).toBeDefined();
    expect(screen.getByText(/under-assessment/)).toBeDefined();
  });

  it('asks for a password to submit to a regulator', async () => {
    render(
      <MarketApprovals version={subject} markets={markets} {...platform()} onDone={vi.fn()} />,
    );

    await userEvent.click(screen.getByRole('button', { name: /submit/i }));

    expect(await screen.findByLabelText(/password/i)).toBeDefined();
  });

  it('asks for no password to record what a regulator decided', async () => {
    // CAP-LCM-012. Signing a factual entry about somebody else's decision would assert
    // responsibility for a decision this organisation did not take.
    render(
      <MarketApprovals version={subject} markets={markets} {...platform()} onDone={vi.fn()} />,
    );

    await userEvent.click(screen.getByRole('button', { name: /record-approval/i }));

    expect(screen.queryByLabelText(/password/i)).toBeNull();
  });

  it('asks when an approval takes effect, and only for the action that records one', async () => {
    // ADR-029 decision 3: required there, refused everywhere else, never defaulted. Asking for
    // a date the platform would refuse is a form the author cannot submit.
    render(
      <MarketApprovals version={subject} markets={markets} {...platform()} onDone={vi.fn()} />,
    );

    await userEvent.click(screen.getByRole('button', { name: /record-approval/i }));
    expect(await screen.findByLabelText(/takes effect/i)).toBeDefined();

    await userEvent.click(screen.getByRole('button', { name: /cancel/i }));
    await userEvent.click(screen.getByRole('button', { name: /record-rejection/i }));
    expect(screen.queryByLabelText(/takes effect/i)).toBeNull();
  });

  it('sends the effective date the author gave', async () => {
    const acting = platform();
    render(<MarketApprovals version={subject} markets={markets} {...acting} onDone={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /record-approval/i }));
    await userEvent.type(await screen.findByLabelText(/takes effect/i), '2026-09-01');
    await userEvent.click(screen.getByRole('button', { name: /^confirm record-approval$/i }));

    expect(acting.marketTransition).toHaveBeenCalledWith(
      'doc-1', 2, 'DE',
      expect.objectContaining({ action: 'record-approval', effectiveFrom: '2026-09-01' }),
    );
  });

  it('shows the platform its own words when it refuses', async () => {
    const acting = {
      ...platform(),
      marketTransition: vi.fn(async () => ({
        ok: false as const,
        detail: 'an author may not deal with a regulator.',
      })),
    };
    render(<MarketApprovals version={subject} markets={markets} {...acting} onDone={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /record-rejection/i }));
    await userEvent.click(screen.getByRole('button', { name: /^confirm record-rejection$/i }));

    expect(await screen.findByText(/may not deal with a regulator/)).toBeDefined();
  });

  it('says a market with nothing to do has nothing to do', async () => {
    render(
      <MarketApprovals
        version={subject}
        markets={{
          EU: {
            state: 'withdrawn',
            actions: [],
            signedActions: [],
            actionsNeedingEffectiveDate: [],
            signatureMeanings: {},
          },
        }}
        {...platform()}
        onDone={vi.fn()}
      />,
    );

    expect(screen.getByText(/nothing to do/i)).toBeDefined();
  });

  it('signs with the meaning the platform configured, not one of its own', async () => {
    // A signature that says the wrong thing is worse than none: the gate refuses it, and the
    // record would have asserted something nobody intended (ADR-020). This was a literal that
    // happened to match, and would have stopped matching the moment a deployment configured a
    // different meaning.
    const acting = platform();
    render(<MarketApprovals version={subject} markets={markets} {...acting} onDone={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /^submit$/i }));
    await userEvent.type(await screen.findByLabelText(/password/i), 'a-password');
    await userEvent.click(screen.getByRole('button', { name: /^confirm submit$/i }));

    expect(acting.sign).toHaveBeenCalledWith(
      expect.objectContaining({ meaning: 'responsibility' }));
  });
});
