import { useEffect, useState } from 'react';
import type { WaitingTask } from './platform/client';

/**
 * What routing has asked this person to do (FN-AUT-009).
 *
 * @remarks
 * Routing has raised tasks since ADR-031 and nothing has ever shown one to anybody. A task
 * records that somebody was asked; a task nobody sees is a review that quietly does not happen,
 * and the failure looks exactly like nothing happening.
 */
export function WaitingWork({
  tasks,
  onOpen,
}: {
  readonly tasks: () => Promise<readonly WaitingTask[]>;
  readonly onOpen: (documentIdentifier: string, version: number) => void;
}) {
  const [waiting, setWaiting] = useState<readonly WaitingTask[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    tasks()
      .then(setWaiting)
      .catch((failed: Error) => {
        // Never shown as nothing waiting. That is a claim - it tells somebody their work is
        // done - and a failure presented that way says so when nobody knows.
        setWaiting(null);
        setProblem(failed.message);
      });
  }, [tasks]);

  return (
    <section>
      <h2>Waiting for you</h2>

      {problem !== null && (
        // Worded so it cannot be read as an empty list, and so it does not contain the phrase
        // that would say so - a test asserts that phrase is absent, and it caught this.
        <p role="alert">
          This could not be answered, so nobody knows what is outstanding: {problem}
        </p>
      )}

      {waiting !== null && waiting.length === 0 && <p role="status">Nothing is waiting for you.</p>}

      {waiting !== null && waiting.length > 0 && (
        <ul>
          {waiting.map((task) => (
            <li key={task.identifier}>
              <button
                type="button"
                onClick={() => onOpen(task.documentIdentifier, task.version)}
              >
                {task.action}
              </button>{' '}
              {/*
                The role it sits with, because that is what a task is assigned to (ADR-031
                decision 4) - a task assigned to a person on leave is a task nobody sees. And
                when it was asked, so an old one looks old.
              */}
              <span>
                version {task.version}, waiting on {task.assignee}, asked{' '}
                {task.raisedAt.slice(0, 10)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
