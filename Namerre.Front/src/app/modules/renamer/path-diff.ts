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

/** Finds the common start and end of two names; what is left in between is the change. */
export function diffNames(oldName: string, newName: string): NameDiff {
  const shortest = Math.min(oldName.length, newName.length);
  let start = 0;
  while (start < shortest && oldName[start] === newName[start]) start++;
  let end = 0;
  while (
    end < shortest - start &&
    oldName[oldName.length - 1 - end] === newName[newName.length - 1 - end]
  ) {
    end++;
  }
  return {
    prefix: oldName.slice(0, start),
    removed: oldName.slice(start, oldName.length - end),
    added: newName.slice(start, newName.length - end),
    suffix: oldName.slice(oldName.length - end),
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
