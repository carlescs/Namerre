import vectors from './test-vectors/tmdb-filename.json';
import { tmdbFilename } from './tmdb-filename';

describe('TMDB filename inference', () => {
  it('cleans movie release names and extracts the release year', () => {
    expect(tmdbFilename('Movies/The.Movie.2024.1080p.WEB-DL.x264.mkv', 'movie')).toEqual({
      query: 'The Movie',
      year: '2024',
    });
    expect(tmdbFilename('1917 (2019).mkv', 'movie')).toEqual({
      query: '1917',
      year: '2019',
    });
    expect(tmdbFilename('2001.A.Space.Odyssey.mkv', 'movie').query).toBe('2001 A Space Odyssey');
  });

  it('extracts each episode independently and supports specials and alternate numbering', () => {
    expect(tmdbFilename('TV/A.Show.S02E03.1080p.mkv', 'tv')).toEqual({
      query: 'A Show',
      season: 2,
      episode: 3,
    });
    expect(tmdbFilename('A Show - 0x02 - Special.mkv', 'tv')).toEqual({
      query: 'A Show',
      season: 0,
      episode: 2,
    });
    expect(tmdbFilename('A Show/Season 02/S02E04.mkv', 'tv')).toEqual({
      query: 'A Show',
      season: 2,
      episode: 4,
    });
  });

  it('does not invent episode numbers when they cannot be inferred', () => {
    expect(tmdbFilename('A.Show.Pilot.mkv', 'tv')).toEqual({ query: 'A Show Pilot' });
  });

  it('only parses episode numbers for TV searches', () => {
    expect(tmdbFilename('Shows/A.Show.S01E02.mkv', 'movie')).toEqual({ query: 'A Show S01E02' });
  });

  it('falls back to the nearest non-season folder for bare episode names', () => {
    expect(tmdbFilename('Library/A_Show (2010)/S1/1x05.mkv', 'tv')).toEqual({
      query: 'A Show',
      year: '2010',
      season: 1,
      episode: 5,
    });
    expect(tmdbFilename('S01E01.mkv', 'tv')).toEqual({ query: '', season: 1, episode: 1 });
    expect(tmdbFilename('Season 1/s01e01.mkv', 'tv')).toEqual({
      query: '',
      season: 1,
      episode: 1,
    });
  });

  it('keeps names without extensions and trims separators', () => {
    expect(tmdbFilename('Movies/- The_Movie [2019] -', 'movie')).toEqual({
      query: 'The Movie',
      year: '2019',
    });
    expect(tmdbFilename('', 'movie')).toEqual({ query: '' });
  });

  it('uses the last plausible year so titles containing years are preserved', () => {
    expect(tmdbFilename('Blade.Runner.2049.2017.2160p.mkv', 'movie')).toEqual({
      query: 'Blade Runner 2049',
      year: '2017',
    });
  });

  it('reads multi-episode ranges in common styles', () => {
    for (const name of [
      'Show.S01E04-E05.mkv',
      'Show.S01E04E05.mkv',
      'Show S01E04-05 1080p.mkv',
      'Show.1x04-1x05.mkv',
      'Show.1x04-05.mkv',
    ]) {
      expect(tmdbFilename(name, 'tv')).toEqual({
        query: 'Show',
        season: 1,
        episode: 4,
        episodeEnd: 5,
      });
    }
  });

  it('does not mistake resolutions, years or backward numbers for ranges', () => {
    for (const name of [
      'Show.S01E04-1080p.mkv',
      'Show.S01E04-720p.mkv',
      'Show.S01E04-2019.mkv',
      'Show.S01E05-E04.mkv',
    ]) {
      expect(tmdbFilename(name, 'tv')).toEqual({
        query: 'Show',
        season: 1,
        episode: name.includes('E05-') ? 5 : 4,
      });
    }
  });
});

describe('TMDB filename shared test vectors', () => {
  // The same vectors run against the server's TmdbFileName.Parse (MirroredLogicTests).
  it.each(vectors)('parses $path as $mediaType', ({ path, mediaType, expected }) => {
    const parsed = tmdbFilename(path, mediaType);
    expect({
      query: parsed.query,
      year: parsed.year ?? null,
      season: parsed.season ?? null,
      episode: parsed.episode ?? null,
      episodeEnd: parsed.episodeEnd ?? null,
    }).toEqual(expected);
  });
});
