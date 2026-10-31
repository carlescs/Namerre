import type { PluginOptions, TmdbMatch } from './rename-api';

// Fake metadata used only to illustrate templates; the server renders the confirmed match.
export const sampleMovie: TmdbMatch = {
  id: 12345,
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
  id: 67890,
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

export const sampleDoubleEpisode: TmdbMatch = {
  ...sampleEpisode,
  episode: 9,
  episodeEnd: 10,
  episodeTitle: 'The Long Goodbye',
  airDate: '2021-12-03',
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
    const range = options.episodeRange ?? 'repeat';
    const stem = render(nameTemplate.trim() ? nameTemplate : defaultTemplate, match, range);
    if (!stem) throw new Error('The plugin produced an empty name.');
    let folder = '';
    if (options.changeFolder) {
      const baseFolder = options.baseFolder ?? '';
      folder =
        (baseFolder ? `${baseFolder}/` : '') +
        render(options.folderTemplate ?? '{title} ({year})', match, range) +
        '/';
      if (options.seasonFolder && match.season !== null) {
        folder += `${render(options.seasonFolderTemplate ?? 'Season {season}', match, range)}/`;
      }
    }
    return { path: folder + stem + extension };
  } catch (error) {
    return { error: (error as Error).message };
  }
}

// `{{` and `}}` are literal braces (Plex reads ids such as `{tmdb-603}`), like TmdbPlugin.Render.
function render(template: string, match: TmdbMatch, range: string): string {
  if (template.length > 1000) throw new Error('Template is too long.');
  let result = '';
  for (let i = 0; i < template.length; i++) {
    const c = template[i];
    if ((c === '{' || c === '}') && template[i + 1] === c) {
      result += c;
      i++;
    } else if (c === '}') {
      throw new Error('Write }} for a literal closing brace.');
    } else if (c === '{') {
      const end = template.indexOf('}', i + 1);
      const open = template.indexOf('{', i + 1);
      if (end < 0 || (open >= 0 && open < end) || end === i + 1) {
        throw new Error('Close each field with }, or write {{ for a literal brace.');
      }
      result += field(template.slice(i + 1, end), match, range);
      i = end;
    } else {
      result += c;
    }
  }
  if (/[/\\]/.test(result)) throw new Error('Templates must render a single path segment.');
  return result;
}

function field(token: string, match: TmdbMatch, range: string): string {
  const colon = token.indexOf(':');
  const name = colon < 0 ? token : token.slice(0, colon);
  const format = colon < 0 ? undefined : token.slice(colon + 1);
  switch (name) {
    case 'season':
      return formatNumber(available(match.season, 'Season'), format, '00');
    case 'seasonNumber':
      return formatNumber(available(match.season, 'Season'), format, '0');
    case 'episode':
      return episodes(match, format, range);
    case 'tmdbId':
      return formatNumber(match.id, format, '0');
  }
  if (format !== undefined) throw new Error('Only numeric fields take a format.');
  switch (name) {
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
      throw new Error(`Unknown template field {${name}}.`);
  }
}

// Mirrors TmdbPlugin.Episodes: a multi-episode match writes both ends in the chosen style.
function episodes(match: TmdbMatch, format: string | undefined, range: string): string {
  const episode = available(match.episode, 'Episode');
  const first = formatNumber(episode, format, '00');
  const end = match.episodeEnd;
  if (end === null || end === undefined || end === episode) return first;
  const last = formatNumber(end, format, '00');
  switch (range) {
    case 'concatenate':
      return `${first}E${last}`;
    case 'hyphen':
      return `${first}-${last}`;
    case 'repeat':
      return `${first}-E${last}`;
    default:
      throw new Error('Choose how multi-episode numbers are written.');
  }
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
