import { RunView } from '../../rename-api';

export type StatusTone = 'green' | 'blue' | 'amber' | 'red' | 'teal' | 'slate';

const LABELS: Record<string, string> = {
  Queued: 'Waiting',
  Running: 'In progress',
  Completed: 'Completed',
  Failed: 'Failed',
  RecoveryRequired: 'Needs recovery',
  RecoveryQueued: 'Recovery waiting',
  Recovering: 'Recovering',
  Recovered: 'Recovered',
  Pending: 'Waiting',
  Staged: 'In progress',
  Uncertain: 'Needs checking',
  Interrupted: 'Interrupted',
  Skipped: 'Skipped',
  Restored: 'Restored',
  RolledBack: 'Undone',
};

const TONES: Record<string, StatusTone> = {
  Completed: 'green',
  Queued: 'blue',
  Running: 'blue',
  RecoveryQueued: 'blue',
  Recovering: 'blue',
  Pending: 'blue',
  Staged: 'blue',
  RecoveryRequired: 'amber',
  Uncertain: 'amber',
  Interrupted: 'amber',
  Skipped: 'amber',
  Failed: 'red',
  Recovered: 'teal',
  Restored: 'teal',
  RolledBack: 'teal',
};

const PILL: Record<StatusTone, string> = {
  green: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-500/15 dark:text-emerald-200',
  blue: 'bg-sky-100 text-sky-800 dark:bg-sky-500/15 dark:text-sky-200',
  amber: 'bg-amber-100 text-amber-900 dark:bg-amber-500/15 dark:text-amber-200',
  red: 'bg-rose-100 text-rose-800 dark:bg-rose-500/15 dark:text-rose-200',
  teal: 'bg-teal-100 text-teal-800 dark:bg-teal-500/15 dark:text-teal-200',
  slate: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300',
};

/** File states that need no further attention, so staging locations are noise. */
const FINAL_FILE_STATES = ['Completed', 'Restored', 'RolledBack'];

export function statusLabel(status: string): string {
  return LABELS[status] ?? status;
}

export function statusClasses(status: string): string {
  return PILL[TONES[status] ?? 'slate'];
}

export function kindLabel(kind: string): string {
  return kind === 'Rollback' ? 'Undo' : kind;
}

export function fileCount(count: number): string {
  return `${count} ${count === 1 ? 'file' : 'files'}`;
}

export function splitPath(path: string): { folder: string; name: string } {
  const index = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  return index < 0
    ? { folder: '', name: path }
    : { folder: path.slice(0, index), name: path.slice(index + 1) };
}

function commonFolder(paths: string[]): string {
  const folders = paths.map((path) => splitPath(path).folder.split(/[\\/]/).filter(Boolean));
  if (!folders.length) return '';
  const first = folders[0];
  let length = first.length;
  for (const folder of folders) {
    let shared = 0;
    while (shared < length && shared < folder.length && folder[shared] === first[shared]) shared++;
    length = shared;
  }
  return first.slice(0, length).join('/');
}

/** Readable name for a run: the folder its files live in (the restored side for an undo). */
export function runTitle(run: RunView): string {
  const undo = run.kind === 'Rollback';
  const paths = run.files.map((file) => (undo ? file.targetPath : file.sourcePath));
  const folder = commonFolder(paths) || (paths[0] ? splitPath(paths[0]).folder : '');
  return folder || 'Top-level folder';
}

/** Whether staging locations are worth showing: a file is not finished or the run needs recovery. */
export function showsStaging(run: RunView): boolean {
  return (
    run.status === 'RecoveryRequired' ||
    run.files.some((file) => !FINAL_FILE_STATES.includes(file.status))
  );
}

const PLUGIN_NAMES: Record<string, string> = {
  literal: 'Find and replace',
  regex: 'Pattern replace',
  case: 'Letter case',
  tmdb: 'Movie and TV lookup',
};

export function pluginName(typeId: string): string {
  return PLUGIN_NAMES[typeId] ?? typeId;
}

/** Plugin options as readable label/value pairs, skipping anything unset. */
export function optionEntries(options: object): { label: string; value: string }[] {
  return Object.entries(options)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(([key, value]) => {
      const spaced = key.replace(/([A-Z])/g, ' $1').toLowerCase();
      return {
        label: spaced.charAt(0).toUpperCase() + spaced.slice(1),
        value: typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value),
      };
    });
}

export interface HistoryEntry {
  run: RunView;
  /** Set for an undo shown beneath the rename it reverts. */
  nested: boolean;
  /** Title of the original rename when it is on the same page. */
  parentTitle: string;
}

/** Orders a page so each undo directly follows the rename it reverts when both are present. */
export function historyEntries(items: RunView[]): HistoryEntry[] {
  const ids = new Set(items.map((item) => item.id));
  const titles = new Map(items.map((item) => [item.id, runTitle(item)]));
  const nestedUnder = (item: RunView) =>
    item.parentRunId && ids.has(item.parentRunId) ? item.parentRunId : null;
  const entries: HistoryEntry[] = [];
  for (const item of items) {
    if (nestedUnder(item)) continue;
    entries.push({ run: item, nested: false, parentTitle: '' });
    for (const child of items) {
      if (nestedUnder(child) === item.id) {
        entries.push({ run: child, nested: true, parentTitle: titles.get(item.id) ?? '' });
      }
    }
  }
  return entries;
}
