import { formatFileSize } from './file-size';

describe('formatFileSize', () => {
  it.each([
    [0, '0 B'],
    [999, '999 B'],
    [1024, '1.0 KB'],
    [1536, '1.5 KB'],
    [12_345, '12 KB'],
    [304_311_922, '290 MB'],
    [4_500_000_000, '4.2 GB'],
    [1_048_575, '1.0 MB'],
    [-1, ''],
  ])('formats %d bytes as "%s"', (bytes, expected) => {
    expect(formatFileSize(bytes)).toBe(expected);
  });
});
