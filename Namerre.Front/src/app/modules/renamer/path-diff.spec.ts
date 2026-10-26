import { describeChange, diffNames, splitPath } from './path-diff';

describe('path-diff', () => {
  it('splits folder and name', () => {
    expect(splitPath('a/b/c.mkv')).toEqual({ folder: 'a/b', name: 'c.mkv' });
    expect(splitPath('c.mkv')).toEqual({ folder: '', name: 'c.mkv' });
  });

  it('finds the changed middle between a common start and end', () => {
    expect(diffNames('Movie 2020.mkv', 'Movie (2020).mkv')).toEqual({
      prefix: 'Movie ',
      removed: '2020',
      added: '(2020)',
      suffix: '.mkv',
    });
    expect(diffNames('abc.mkv', 'xyz.mkv')).toEqual({
      prefix: '',
      removed: 'abc',
      added: 'xyz',
      suffix: '.mkv',
    });
  });

  it('never lets the start and end overlap', () => {
    expect(diffNames('aaa', 'aa')).toEqual({ prefix: 'aa', removed: 'a', added: '', suffix: '' });
    expect(diffNames('aa', 'aaa')).toEqual({ prefix: 'aa', removed: '', added: 'a', suffix: '' });
    expect(diffNames('same.txt', 'same.txt')).toEqual({
      prefix: 'same.txt',
      removed: '',
      added: '',
      suffix: '',
    });
  });

  it('keeps astral characters whole', () => {
    expect(diffNames('😀.mkv', '😁.mkv')).toEqual({
      prefix: '',
      removed: '😀',
      added: '😁',
      suffix: '.mkv',
    });
  });

  it('reports folder and name changes separately', () => {
    const moved = describeChange('a/x.mkv', 'b/x.mkv');
    expect(moved.folderChanged).toBe(true);
    expect(moved.nameChanged).toBe(false);
    const renamed = describeChange('a/x.mkv', 'a/y.mkv');
    expect(renamed.folderChanged).toBe(false);
    expect(renamed.nameChanged).toBe(true);
  });
});
