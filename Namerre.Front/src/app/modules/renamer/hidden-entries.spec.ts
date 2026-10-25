import { isHiddenEntry, isInsideHiddenFolder } from './hidden-entries';

describe('hidden entries', () => {
  it.each(['.DS_Store', '.scryer-recycle', '@eaDir', 'Thumbs.db', 'desktop.ini', '#recycle'])(
    'treats %s as hidden',
    (name) => expect(isHiddenEntry(name)).toBe(true),
  );

  it.each(['Movie (2019).mkv', 'Season 1', 'poster.jpg', 'S01E01.en.srt'])(
    'keeps %s visible',
    (name) => expect(isHiddenEntry(name)).toBe(false),
  );

  it('checks only the folders below the selected base', () => {
    expect(isInsideHiddenFolder('Series/Show/@eaDir', 'Series/Show')).toBe(true);
    expect(isInsideHiddenFolder('Series/Show/S01/@eaDir/x', 'Series/Show')).toBe(true);
    expect(isInsideHiddenFolder('Series/Show/S01', 'Series/Show')).toBe(false);
    expect(isInsideHiddenFolder('@eaDir', '')).toBe(true);
    expect(isInsideHiddenFolder('.hidden/Show', '.hidden')).toBe(false);
    expect(isInsideHiddenFolder('.hidden', '.hidden')).toBe(false);
  });
});
