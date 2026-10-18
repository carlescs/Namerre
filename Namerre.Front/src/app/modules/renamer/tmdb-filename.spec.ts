import { preferredTmdbMatch, tmdbFilename } from './tmdb-filename';
import { TmdbMatch } from './rename-api';

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

  it('prefers a matching title and year over TMDB result order', () => {
    const match: TmdbMatch = {
      id: 1,
      mediaType: 'movie',
      title: 'The Movie',
      year: '1990',
      season: null,
      episode: null,
      episodeTitle: null,
      confirmationToken: null,
    };
    const correct = { ...match, id: 2, year: '2024' };
    expect(preferredTmdbMatch([match, correct], { query: 'The Movie', year: '2024' })).toBe(
      correct,
    );
    expect(preferredTmdbMatch([], { query: 'Missing' })).toBeUndefined();
  });
});
