import { sampleEpisode, sampleMovie, tmdbTemplatePreview } from './tmdb-template-preview';

describe('TMDB template preview', () => {
  it('uses the media-specific default when the filename template is empty', () => {
    expect(tmdbTemplatePreview({ nameTemplate: '' }, sampleMovie)).toEqual({
      path: 'Sample Movie (2024).mkv',
    });
    expect(tmdbTemplatePreview({ nameTemplate: ' ' }, sampleEpisode)).toEqual({
      path: 'Sample Show - S02E05 - The Example Episode.mkv',
    });
  });

  it('renders base, show and season folders only when folder changes are enabled', () => {
    const options = {
      nameTemplate: '{title} {seasonNumber}x{episode}',
      changeFolder: true,
      baseFolder: 'TV',
      folderTemplate: '{title}',
      seasonFolder: true,
      seasonFolderTemplate: 'Season {seasonNumber}',
    };
    expect(tmdbTemplatePreview(options, sampleEpisode)).toEqual({
      path: 'TV/Sample Show/Season 2/Sample Show 2x05.mkv',
    });
    expect(tmdbTemplatePreview({ ...options, nameTemplate: '{title}' }, sampleMovie)).toEqual({
      path: 'TV/Sample Movie/Sample Movie.mkv',
    });
    expect(tmdbTemplatePreview({ ...options, changeFolder: false }, sampleEpisode)).toEqual({
      path: 'Sample Show 2x05.mkv',
    });
  });

  it('reports the same template errors the server would', () => {
    expect(tmdbTemplatePreview({ nameTemplate: '{title} S{season}' }, sampleMovie).error).toBe(
      'Season is unavailable.',
    );
    expect(tmdbTemplatePreview({ nameTemplate: '{rating}' }, sampleMovie).error).toBe(
      'Unknown template field {rating}.',
    );
    expect(tmdbTemplatePreview({ nameTemplate: '{title}/x' }, sampleMovie).error).toContain(
      'single path segment',
    );
    expect(tmdbTemplatePreview({ nameTemplate: 'x'.repeat(1001) }, sampleMovie).error).toBe(
      'Template is too long.',
    );
  });

  it('treats doubled braces as literal braces like the server', () => {
    const preview = (nameTemplate: string) => tmdbTemplatePreview({ nameTemplate }, sampleMovie);
    expect(preview('{title} {{tmdb-{tmdbId}}}').path).toBe('Sample Movie {tmdb-12345}.mkv');
    expect(preview('{{{title}}}').path).toBe('{Sample Movie}.mkv');
    for (const template of ['{title}}', '{title', '{}', '{ti{tle}']) {
      expect(preview(template).error).toBeDefined();
    }
  });

  it('renders the extended fields and number formats', () => {
    const preview = (nameTemplate: string, match = sampleEpisode) =>
      tmdbTemplatePreview({ nameTemplate }, { ...match, id: 1396 }).path;
    expect(preview('{title} ({year}) [tmdbid-{tmdbId}]')).toBe(
      'Sample Show (2019) [tmdbid-1396].mkv',
    );
    expect(preview('{originalTitle} {airDate} ({showYear})')).toBe(
      'Sample Show 2021-10-08 (2019).mkv',
    );
    expect(preview('{title} S{season:0}E{episode:000}')).toBe('Sample Show S2E005.mkv');
    expect(preview('{seasonNumber:00}x{episode:0} {tmdbId:000000}')).toBe('02x5 001396.mkv');
    expect(preview('{originalTitle} [{airDate}]', sampleMovie)).toBe(
      'Sample Movie [2024-03-15].mkv',
    );
    expect(preview('{originalTitle}', { ...sampleMovie, originalTitle: 'A: B' })).toBe('A- B.mkv');
  });

  it('reports unavailable extended fields and invalid number formats', () => {
    const error = (nameTemplate: string, match = sampleMovie) =>
      tmdbTemplatePreview({ nameTemplate }, match).error;
    expect(error('{showYear}')).toBe('Show year is unavailable.');
    expect(error('{airDate}', { ...sampleMovie, airDate: null })).toBe('Air date is unavailable.');
    expect(error('{originalTitle}', { ...sampleMovie, originalTitle: undefined })).toBe(
      'Original title is unavailable.',
    );
    expect(error('{episode:000}')).toBe('Episode is unavailable.');
    expect(error('{title:00}')).toBe('Only numeric fields take a format.');
    for (const format of ['', '0000000', '#0', '00a']) {
      expect(error(`{tmdbId:${format}}`)).toBe(
        'Number formats are 1 to 6 zeros, e.g. {episode:000}.',
      );
    }
  });

  it('sanitizes metadata like the server', () => {
    const match = { ...sampleMovie, title: 'A: {B}?. ' };
    expect(tmdbTemplatePreview({ nameTemplate: '{title}' }, match)).toEqual({
      path: 'A- (B)-.mkv',
    });
  });

  it('rejects empty metadata and prefixes reserved names like the server', () => {
    const named = (title: string) =>
      tmdbTemplatePreview({ nameTemplate: '[{title}]' }, { ...sampleMovie, title });
    expect(named(' . ').error).toBe('TMDB returned text that cannot form a filename.');
    expect(named('CON')).toEqual({ path: '[_CON].mkv' });
    expect(named('com1.txt')).toEqual({ path: '[_com1.txt].mkv' });
    expect(named('.namerre-stage')).toEqual({ path: '[_.namerre-stage].mkv' });
    expect(named('Console')).toEqual({ path: '[Console].mkv' });
  });

  it('writes multi-episode ranges in the chosen style like the server', () => {
    const match = { ...sampleEpisode, episode: 1, episodeEnd: 2, episodeTitle: 'A + B' };
    expect(tmdbTemplatePreview({ nameTemplate: '' }, match).path).toBe(
      'Sample Show - S02E01-E02 - A + B.mkv',
    );
    expect(tmdbTemplatePreview({ episodeRange: 'concatenate' }, match).path).toBe(
      'Sample Show - S02E01E02 - A + B.mkv',
    );
    expect(
      tmdbTemplatePreview({ nameTemplate: '{episode:000}', episodeRange: 'hyphen' }, match).path,
    ).toBe('001-002.mkv');
    expect(tmdbTemplatePreview({ episodeRange: 'hyphen' }, sampleEpisode).path).toBe(
      'Sample Show - S02E05 - The Example Episode.mkv',
    );
    expect(tmdbTemplatePreview({ episodeRange: 'other' }, match).error).toBe(
      'Choose how multi-episode numbers are written.',
    );
  });
});
