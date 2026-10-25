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
});
