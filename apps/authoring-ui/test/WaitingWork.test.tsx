import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { WaitingWork } from '../src/WaitingWork';

// What routing has asked this person to do (FN-AUT-009).
//   CAP-WFL-001 Configurable multi-step review and approval workflows
//   CAP-WFL-002 Task assignment, reassignment, escalation and due dates
//
// Routing has raised tasks since ADR-031 and nothing has ever shown one to anybody. A task
// records that somebody was asked; a task nobody sees is a review that quietly does not happen,
// and the failure looks exactly like nothing happening.
describe('FN-AUT-009 what is waiting', () => {
  const task = {
    identifier: 'task-1',
    documentIdentifier: '01a00000-0000-7000-8000-00000000000a',
    version: 2,
    action: 'approve',
    assignee: 'approver',
    raisedAt: '2026-08-16T09:00:00Z',
  };

  it('lists what this person has been asked to do', async () => {
    render(<WaitingWork tasks={vi.fn(async () => [task])} onOpen={vi.fn()} />);

    // By role, because "approver" beside it also matches the word.
    expect(await screen.findByRole('button', { name: 'approve' })).toBeDefined();
  });

  it('opens the version a task is about', async () => {
    // The point of showing it. A task naming a label that cannot be reached from it is a
    // reminder rather than a piece of work.
    const onOpen = vi.fn();
    render(<WaitingWork tasks={vi.fn(async () => [task])} onOpen={onOpen} />);

    await userEvent.click(await screen.findByRole('button', { name: /approve/i }));

    expect(onOpen).toHaveBeenCalledWith('01a00000-0000-7000-8000-00000000000a', 2);
  });

  it('says who it is waiting on, because a task is assigned to a role', async () => {
    // ADR-031 decision 4: a task assigned to a person on leave is a task nobody sees. It sits
    // with a role, and whoever holds that role should be able to tell it is theirs.
    render(<WaitingWork tasks={vi.fn(async () => [task])} onOpen={vi.fn()} />);

    expect(await screen.findByText(/approver/)).toBeDefined();
  });

  it('says nothing is waiting, when nothing is', async () => {
    render(<WaitingWork tasks={vi.fn(async () => [])} onOpen={vi.fn()} />);

    expect(await screen.findByText(/nothing is waiting/i)).toBeDefined();
  });

  it('never shows a failure as nothing waiting', async () => {
    // The one that matters. "Nothing waiting" is a claim - it tells somebody their work is done
    // - and a failure presented that way says so when nobody knows.
    const failing = vi.fn(async () => {
      throw new Error('The platform answered 503.');
    });
    render(<WaitingWork tasks={failing} onOpen={vi.fn()} />);

    expect(await screen.findByRole('alert')).toBeDefined();
    expect(screen.queryByText(/nothing is waiting/i)).toBeNull();
  });

  it('says when each was asked, so an old one looks old', async () => {
    render(<WaitingWork tasks={vi.fn(async () => [task])} onOpen={vi.fn()} />);

    expect(await screen.findByText(/2026-08-16/)).toBeDefined();
  });
});
