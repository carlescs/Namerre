import type { RunView } from '../../rename-api';
import {
  historyEntries,
  isFinishingUp,
  optionEntries,
  runStatusLabel,
  runTitle,
  splitPath,
  statusLabel,
} from './run-display';

function run(partial: Partial<RunView>, paths: [string, string][]): RunView {
  return {
    id: 'r',
    planId: 'p',
    parentRunId: null,
    kind: 'Rename',
    status: 'Completed',
    files: paths.map(([sourcePath, targetPath], index) => ({
      id: String(index),
      sourcePath,
      targetPath,
      stagingPath: '',
      hash: '',
      status: 'Completed',
      error: null,
    })),
    journal: [],
    createdAt: '2026-10-18T00:00:00Z',
    error: null,
    ...partial,
  };
}

describe('run display helpers', () => {
  it('uses the common folder, the first folder otherwise, and the target side for an undo', () => {
    const files: [string, string][] = [
      ['Movies/A/x.mkv', 'Films/B/x.mkv'],
      ['Movies/A/B/y.mkv', 'Films/B/y.mkv'],
    ];
    expect(runTitle(run({}, files))).toBe('Movies/A');
    expect(runTitle(run({ kind: 'Rollback' }, files))).toBe('Films/B');
    expect(runTitle(run({}, [['Movies/x.mkv', 'z']]))).toBe('Movies');
    expect(runTitle(run({}, [['x.mkv', 'y.mkv']]))).toBe('Top-level folder');
  });

  it('splits folders from file names', () => {
    expect(splitPath('a/b/c.txt')).toEqual({ folder: 'a/b', name: 'c.txt' });
    expect(splitPath('c.txt')).toEqual({ folder: '', name: 'c.txt' });
  });

  it('labels statuses in plain language', () => {
    expect(statusLabel('RecoveryRequired')).toBe('Needs recovery');
    expect(statusLabel('Running')).toBe('In progress');
    expect(statusLabel('Unheard')).toBe('Unheard');
  });

  it('reports finishing up once every moved file is completed but the run is still running', () => {
    const files: [string, string][] = [
      ['a/x.mkv', 'a/y.mkv'],
      ['a/same.mkv', 'a/same.mkv'],
    ];
    const finishing = run({ status: 'Running' }, files);
    finishing.files[1].status = 'Pending';
    expect(isFinishingUp(finishing)).toBe(true);
    expect(runStatusLabel(finishing)).toBe('Finishing up…');
    finishing.files[0].status = 'Staged';
    expect(isFinishingUp(finishing)).toBe(false);
    expect(runStatusLabel(finishing)).toBe('In progress');
    expect(runStatusLabel(run({ status: 'Completed' }, files))).toBe('Completed');
    expect(isFinishingUp(run({ status: 'Running' }, []))).toBe(false);
  });

  it('leaves an undo at the top level when its rename is not on the page', () => {
    const undo = run({ id: 'u', kind: 'Rollback', parentRunId: 'gone' }, [['a/x', 'a/y']]);
    expect(historyEntries([undo]).map((entry) => entry.nested)).toEqual([false]);
  });

  it('lists options as readable pairs without unset values', () => {
    expect(optionEntries({ ignoreCase: true, search: 'a', replacement: '' })).toEqual([
      { label: 'Ignore case', value: 'Yes' },
      { label: 'Search', value: 'a' },
    ]);
  });
});
