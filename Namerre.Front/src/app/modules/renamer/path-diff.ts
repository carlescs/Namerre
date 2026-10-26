export interface SplitPath {
  /** Folder part without the trailing slash; empty at the root. */
  folder: string;
  name: string;
}

export interface NameDiff {
  prefix: string;
  /** Part of the old name that is replaced. */
  removed: string;
  /** Part of the new name that is not in the old name. */
  added: string;
  suffix: string;
}

export interface PathChange {
  from: SplitPath;
  to: SplitPath;
  folderChanged: boolean;
  nameChanged: boolean;
  diff: NameDiff;
}

export function splitPath(path: string): SplitPath {
  const separator = path.lastIndexOf('/');
  return separator < 0
    ? { folder: '', name: path }
    : { folder: path.slice(0, separator), name: path.slice(separator + 1) };
}

/**
 * Finds the common start and end of two names; what is left in between is the change. Works on
 * whole code points so a changed emoji or other astral character is never split in two.
 */
export function diffNames(oldName: string, newName: string): NameDiff {
  const before = Array.from(oldName);
  const after = Array.from(newName);
  const shortest = Math.min(before.length, after.length);
  let start = 0;
  while (start < shortest && before[start] === after[start]) start++;
  let end = 0;
  while (
    end < shortest - start &&
    before[before.length - 1 - end] === after[after.length - 1 - end]
  ) {
    end++;
  }
  return {
    prefix: before.slice(0, start).join(''),
    removed: before.slice(start, before.length - end).join(''),
    added: after.slice(start, after.length - end).join(''),
    suffix: before.slice(before.length - end).join(''),
  };
}

export function describeChange(sourcePath: string, targetPath: string): PathChange {
  const from = splitPath(sourcePath);
  const to = splitPath(targetPath);
  return {
    from,
    to,
    folderChanged: from.folder !== to.folder,
    nameChanged: from.name !== to.name,
    diff: diffNames(from.name, to.name),
  };
}
