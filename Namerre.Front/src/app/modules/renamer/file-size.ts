const units = ['B', 'KB', 'MB', 'GB', 'TB'];

/** Formats a byte count with binary units: "290 MB", one decimal under 10 ("4.2 GB"). */
export function formatFileSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '';
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  // Rounding can reach 1024 (for example 1,048,575 bytes); show it in the next unit instead.
  if (value >= 1023.5 && unit < units.length - 1) {
    value = 1;
    unit++;
  }
  return `${value < 10 ? value.toFixed(1) : Math.round(value)} ${units[unit]}`;
}
