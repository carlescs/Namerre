import { TmdbMatch } from './rename-api';

export interface TmdbFilename {
  query: string;
  year?: string;
  season?: number;
  episode?: number;
}

export function tmdbFilename(path: string, mediaType: string): TmdbFilename {
  const parts = path.split('/');
  const name = (parts.pop() ?? '').replace(/\.[^.]+$/, '');
  const episode =
    mediaType === 'tv' ? /s(\d{1,3})e(\d{1,4})|(\d{1,3})x(\d{1,4})/i.exec(name) : null;
  let title = episode ? name.slice(0, episode.index) : name;
  if (episode && !clean(title)) {
    title = parts.reverse().find((part) => !/^(?:season|s)[ ._-]*\d+$/i.test(part)) ?? '';
  }
  const year = [...title.matchAll(/[ ._([]((?:19|20)\d{2})(?=$|[ ._)\]])/g)].pop();
  if (year) title = title.slice(0, year.index);
  title = title.split(
    /\b(?:480p|720p|1080[pi]|2160p|4k|bluray|blu-ray|web[ .-]?dl|webrip|hdtv|dvdrip|x26[45]|h26[45])\b/i,
  )[0];
  return {
    query: clean(title),
    ...(year ? { year: year[1] } : {}),
    ...(episode
      ? {
          season: Number(episode[1] ?? episode[3]),
          episode: Number(episode[2] ?? episode[4]),
        }
      : {}),
  };
}

export function preferredTmdbMatch(
  matches: TmdbMatch[],
  filename: TmdbFilename,
): TmdbMatch | undefined {
  const normalize = (value: string) => value.toLocaleLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
  const score = (match: TmdbMatch) =>
    (normalize(match.title) === normalize(filename.query) ? 2 : 0) +
    (filename.year && match.year === filename.year ? 1 : 0);
  return [...matches].sort((left, right) => score(right) - score(left))[0];
}

function clean(value: string): string {
  return value
    .replace(/[._]/g, ' ')
    .replace(/[[\]()]/g, ' ')
    .replace(/\s+/g, ' ')
    .replace(/^[\s-]+|[\s-]+$/g, '');
}
