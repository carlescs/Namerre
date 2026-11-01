import { companionSuffix, detectCompanions, isVideo } from './companions';
import vectors from './test-vectors/companions.json';

describe('Related files', () => {
  it.each([
    ['Movie.nfo', '.nfo'],
    ['Movie.srt', '.srt'],
    ['Movie.en.hi.srt', '.en.hi.srt'],
    ['Movie.pt-BR.forced.vtt', '.pt-BR.forced.vtt'],
    ['Movie.eng.ass', '.eng.ass'],
    ['Movie-poster.jpg', '-poster.jpg'],
    ['Movie-fanart.PNG', '-fanart.PNG'],
    ['Movie-thumb.webp', '-thumb.webp'],
    ['Movie 2.srt', null],
    ['Movie2-poster.jpg', null],
    ['Movie.Part.Two.srt', null],
    ['Movie.en.txt', null],
    ['Movie-banner.jpg', null],
    ['Movie.jpg', null],
    ['movie.srt', null],
  ])('recognises %s as %s', (name, expected) => {
    expect(companionSuffix('Movie', name)).toBe(expected);
  });

  it('recognises video files by extension', () => {
    expect(isVideo('Movie.MKV')).toBe(true);
    expect(isVideo('Movie.srt')).toBe(false);
  });

  it('pairs each companion with its own video when stems overlap', () => {
    const companions = detectCompanions([
      'Films/Movie.mkv',
      'Films/Movie 2.mkv',
      'Films/Movie.en.srt',
      'Films/Movie 2.en.srt',
      'Films/Movie 2.nfo',
      'Films/Movie-poster.jpg',
      'Other/Movie.srt',
      'Films/notes.txt',
    ]);
    expect([...companions]).toEqual([
      ['Films/Movie.en.srt', 'Films/Movie.mkv'],
      ['Films/Movie 2.en.srt', 'Films/Movie 2.mkv'],
      ['Films/Movie 2.nfo', 'Films/Movie 2.mkv'],
      ['Films/Movie-poster.jpg', 'Films/Movie.mkv'],
    ]);
  });

  it('prefers the longest video stem', () => {
    expect([...detectCompanions(['Show.mkv', 'Show.en.mkv', 'Show.en.srt'])]).toEqual([
      ['Show.en.srt', 'Show.en.mkv'],
    ]);
  });
});

describe('Related files shared test vectors', () => {
  // The same vectors run against the server's CompanionFiles (MirroredLogicTests).
  it.each(vectors.isVideo)('isVideo($name) is $expected', ({ name, expected }) => {
    expect(isVideo(name)).toBe(expected);
  });

  it.each(vectors.suffix)(
    'companionSuffix("$videoStem", "$name") is $expected',
    ({ videoStem, name, expected }) => {
      expect(companionSuffix(videoStem, name)).toBe(expected);
    },
  );

  it.each(vectors.detect)('detects companions: $name', ({ files, expected }) => {
    expect(Object.fromEntries(detectCompanions(files))).toEqual(expected);
  });
});
