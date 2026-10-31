import type { PluginOptions, TmdbMatch } from './rename-api';

// Fake metadata used only to illustrate templates; the server renders the confirmed match.
export const sampleMovie: TmdbMatch = {
  id: 0,
  mediaType: 'movie',
  title: 'Sample Movie',
  year: '2024',
  season: null,
  episode: null,
  episodeTitle: null,
  confirmationToken: null,
  originalTitle: 'Sample Movie',
  airDate: '2024-03-15',
  showYear: null,
};
export const sampleEpisode: TmdbMatch = {
  id: 0,
  mediaType: 'tv',
  title: 'Sample Show',
  year: '2019',
  season: 2,
  episode: 5,
  episodeTitle: 'The Example Episode',
  confirmationToken: null,
  originalTitle: 'Sample Show',
  airDate: '2021-10-08',
  showYear: '2019',
};

export type TemplatePreview = { path: string; error?: never } | { path?: never; error: string };

// Mirrors PipelineEngine's TMDB rendering so the editor can show what a template produces.
export function tmdbTemplatePreview(
  options: PluginOptions,
  match: TmdbMatch,
  extension = '.mkv',
): TemplatePreview {
  try {
    const defaultTemplate =
      match.mediaType === 'tv'
        ? '{title} - S{season}E{episode} - {episodeTitle}'
        : '{title} ({year})';
    const nameTemplate = options.nameTemplate ?? '';
    const stem = render(nameTemplate.trim() ? nameTemplate : defaultTemplate, match);
    if (!stem) throw new Error('The plugin produced an empty name.');
    let folder = '';
    if (options.changeFolder) {
      const baseFolder = options.baseFolder ?? '';
      folder =
        (baseFolder ? `${baseFolder}/` : '') +
        render(options.folderTemplate ?? '{title} ({year})', match) +
        '/';
      if (options.seasonFolder && match.season !== null) {
        folder += `${render(options.seasonFolderTemplate ?? 'Season {season}', match)}/`;
      }
    }
    return { path: folder + stem + extension };
  } catch (error) {
    return { error: (error as Error).message };
  }
}

function render(template: string, match: TmdbMatch): string {
  if (template.length > 1000) throw new Error('Template is too long.');
  const result = template.replace(/\{([^{}]+)\}/g, (_, token: string) => {
    const colon = token.indexOf(':');
    const field = colon < 0 ? token : token.slice(0, colon);
    const format = colon < 0 ? undefined : token.slice(colon + 1);
    switch (field) {
      case 'season':
        return formatNumber(available(match.season, 'Season'), format, '00');
      case 'seasonNumber':
        return formatNumber(available(match.season, 'Season'), format, '0');
      case 'episode':
        return formatNumber(available(match.episode, 'Episode'), format, '00');
      case 'tmdbId':
        return formatNumber(match.id, format, '0');
    }
    if (format !== undefined) throw new Error('Only numeric fields take a format.');
    switch (field) {
      case 'title':
        return metadataText(match.title);
      case 'originalTitle':
        return metadataText(available(match.originalTitle, 'Original title'));
      case 'year':
        return match.year === null ? '' : metadataText(match.year);
      case 'showYear':
        return metadataText(available(match.showYear, 'Show year'));
      case 'airDate':
        return metadataText(available(match.airDate, 'Air date'));
      case 'episodeTitle':
        return metadataText(available(match.episodeTitle, 'Episode title'));
      default:
        throw new Error(`Unknown template field {${field}}.`);
    }
  });
  if (/[{}/\\]/.test(result)) {
    throw new Error('Templates must render a single path segment without braces.');
  }
  return result;
}

function available<T>(value: T | null | undefined, field: string): T {
  if (value === null || value === undefined) throw new Error(`${field} is unavailable.`);
  return value;
}

// Formats with a zero-padding pattern such as 0, 00 or 000, like the server.
function formatNumber(value: number, format: string | undefined, defaultFormat: string): string {
  const pattern = format ?? defaultFormat;
  if (!/^0{1,6}$/.test(pattern)) {
    throw new Error('Number formats are 1 to 6 zeros, e.g. {episode:000}.');
  }
  return value.toString().padStart(pattern.length, '0');
}

function metadataText(value: string): string {
  const result = [...value]
    .map((c) => (c === '{' ? '(' : c === '}' ? ')' : c < ' ' || '<>:"/\\|?*'.includes(c) ? '-' : c))
    .join('')
    .replace(/[ .]+$/, '');
  if (!result) throw new Error('TMDB returned text that cannot form a filename.');
  return /^\.namerre-/i.test(result) || /^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)/i.test(result)
    ? `_${result}`
    : result;
}
