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
  const pad = (value: number) => value.toString().padStart(2, '0');
  const result = template.replace(/\{([^{}]+)\}/g, (_, field: string) => {
    switch (field) {
      case 'title':
        return metadataText(match.title);
      case 'year':
        return match.year === null ? '' : metadataText(match.year);
      case 'season':
        if (match.season === null) throw new Error('Season is unavailable.');
        return pad(match.season);
      case 'seasonNumber':
        if (match.season === null) throw new Error('Season is unavailable.');
        return match.season.toString();
      case 'episode':
        if (match.episode === null) throw new Error('Episode is unavailable.');
        return pad(match.episode);
      case 'episodeTitle':
        if (match.episodeTitle === null) throw new Error('Episode title is unavailable.');
        return metadataText(match.episodeTitle);
      default:
        throw new Error(`Unknown template field {${field}}.`);
    }
  });
  if (/[{}/\\]/.test(result)) {
    throw new Error('Templates must render a single path segment without braces.');
  }
  return result;
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
