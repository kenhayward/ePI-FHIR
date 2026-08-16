import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { LifecycleActions } from '../src/LifecycleActions';

// Moving a version through its lifecycle, and signing where the gate says so (FN-AUT-010).
//   CAP-WFL-003 Invoke electronic signature at approval gates
//   CAP-LCM-001 Transitions permitted by the state model and nothing else
//
// The platform decides what may happen and refuses what may not (ADR-037 decision 1). What this
// is responsible for is that a refusal reaches the person, in the platform's words, and that a
// password typed to sign a record is not treated like one typed to sign in (ADR-041).
describe('FN-AUT-010 acting on a version', () => {
  const acted = () => ({
    transition: vi.fn(async () => ({ ok: true as const, from: 'draft', to: 'in-review' })),
    sign: vi.fn(async () => ({ refused: false as const, reference: 'sig-1' })),
  });

  const draft = { state: 'draft', documentIdentifier: 'doc-1', version: 2 };

  it('offers what the state model permits from here', async () => {
    render(<LifecycleActions version={draft} actions={['submit']} {...acted()} onDone={vi.fn()} />);

    expect(screen.getByRole('button', { name: /submit/i })).toBeDefined();
  });

  it('moves a version that needs no signature without asking for one', async () => {
    // Submitting is not a signed gate. Asking for a password anyway would teach people to type
    // it whenever they are asked, which is how the one that matters stops being a control.
    const platform = acted();
    render(<LifecycleActions version={draft} actions={['submit']} {...platform} onDone={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /submit/i }));

    expect(screen.queryByLabelText(/password/i)).toBeNull();
    expect(platform.transition).toHaveBeenCalledWith('doc-1', 2, expect.objectContaining({
      action: 'submit',
    }));
  });

  it('asks for a password at a signed gate, and says it is for signing rather than signing in', async () => {
    // ADR-041 decision 1. Re-entering it is the control - it is what makes the signature
    // attributable to a person rather than to a session somebody left open.
    render(
      <LifecycleActions
        version={{ ...draft, state: 'in-review' }}
        actions={['approve']}
        signedActions={['approve']}
        {...acted()}
        onDone={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: /approve/i }));

    expect(await screen.findByLabelText(/password/i)).toBeDefined();
    expect(screen.getByText(/signing this record/i)).toBeDefined();
  });

  it('signs first and cites the reference on the transition', async () => {
    const platform = acted();
    render(
      <LifecycleActions
        version={{ ...draft, state: 'in-review' }}
        actions={['approve']}
        signedActions={['approve']}
        {...platform}
        onDone={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: /approve/i }));
    await userEvent.type(await screen.findByLabelText(/password/i), 'a-password');
    await userEvent.click(screen.getByRole('button', { name: /sign and approve/i }));

    expect(platform.sign).toHaveBeenCalledWith(expect.objectContaining({ password: 'a-password' }));
    expect(platform.transition).toHaveBeenCalledWith('doc-1', 2, expect.objectContaining({
      signatureReference: 'sig-1',
    }));
  });

  it('does not attempt the transition when the signature was refused', async () => {
    // A wrong password must not become a transition that fails for a reason nobody can act on.
    const platform = {
      ...acted(),
      sign: vi.fn(async () => ({
        refused: true as const,
        detail: 'those credentials were not accepted',
      })),
    };
    render(
      <LifecycleActions
        version={{ ...draft, state: 'in-review' }}
        actions={['approve']}
        signedActions={['approve']}
        {...platform}
        onDone={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: /approve/i }));
    await userEvent.type(await screen.findByLabelText(/password/i), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: /sign and approve/i }));

    expect(await screen.findByRole('alert')).toBeDefined();
    expect(platform.transition).not.toHaveBeenCalled();
  });

  it('shows the platform its own words when it refuses a transition', async () => {
    // Segregation of duties is the case that matters: an author who wrote a version may not
    // approve it, and being told exactly that is far more use than "forbidden".
    const platform = {
      ...acted(),
      transition: vi.fn(async () => ({
        ok: false as const,
        detail: 'the author of a version may not approve it.',
      })),
    };
    render(<LifecycleActions version={draft} actions={['submit']} {...platform} onDone={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: /submit/i }));

    expect(await screen.findByText(/may not approve it/)).toBeDefined();
  });

  it('keeps the password nowhere once it has been used', async () => {
    // ADR-041 decision 2. It exists for one request. Anything holding it afterwards is a
    // credential sitting in a browser for as long as the tab is open.
    const platform = acted();
    render(
      <LifecycleActions
        version={{ ...draft, state: 'in-review' }}
        actions={['approve']}
        signedActions={['approve']}
        {...platform}
        onDone={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: /approve/i }));
    const box = await screen.findByLabelText(/password/i);
    await userEvent.type(box, 'a-password');
    await userEvent.click(screen.getByRole('button', { name: /sign and approve/i }));

    expect(screen.queryByLabelText(/password/i)).toBeNull();
    for (const store of [localStorage, sessionStorage]) {
      for (let i = 0; i < store.length; i++) {
        expect(store.getItem(store.key(i)!)).not.toContain('a-password');
      }
    }
  });

  it('says what happened when a version moves', async () => {
    const onDone = vi.fn();
    render(<LifecycleActions version={draft} actions={['submit']} {...acted()} onDone={onDone} />);

    await userEvent.click(screen.getByRole('button', { name: /submit/i }));

    expect(await screen.findByText(/in-review/)).toBeDefined();
    expect(onDone).toHaveBeenCalled();
  });
});
