// Names that operating systems and NAS software create next to media: dotfiles (.DS_Store),
// Synology metadata (@eaDir) and the usual Windows and recycle-bin entries.
const systemNames = new Set([
  '#recycle',
  '#snapshot',
  '$recycle.bin',
  'desktop.ini',
  'lost+found',
  'system volume information',
  'thumbs.db',
]);

export function isHiddenEntry(name: string): boolean {
  return name.startsWith('.') || name.startsWith('@') || systemNames.has(name.toLowerCase());
}

/** Whether any folder below `base` on the way to `path` is hidden. */
export function isInsideHiddenFolder(path: string, base: string): boolean {
  const relative = base && path.startsWith(`${base}/`) ? path.slice(base.length + 1) : path;
  return path !== base && relative.split('/').some(isHiddenEntry);
}
