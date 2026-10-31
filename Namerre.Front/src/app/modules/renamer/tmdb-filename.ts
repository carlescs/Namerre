import type { TmdbMatch } from './rename-api';

export interface TmdbFilename {
  query: string;
  year?: string;
  season?: number;
  episode?: number;
  /** Last episode of a multi-episode name such as S01E01-E02, S01E01E02, S01E01-02 or 1x01-1x02. */
  episodeEnd?: number;
}

export function tmdbFilename(path: string, mediaType: string): TmdbFilename {
  const parts = path.split('/');
  const name = (parts.pop() ?? '').replace(/\.[^.]+$/, '');
  const episode =
    mediaType === 'tv'
      ? /s(\d{1,3})e(\d{1,4})(?:-?e(\d{1,4})|-(\d{1,4})(?![\dp]))?|(\d{1,3})x(\d{1,4})(?:-(?:\d{1,3}x)?(\d{1,4})(?![\dp]))?/i.exec(
          name,
        )
      : null;
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
    ...(episode ? episodeNumbers(episode) : {}),
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

function episodeNumbers(
  match: RegExpExecArray,
): Pick<TmdbFilename, 'season' | 'episode' | 'episodeEnd'> {
  const season = Number(match[1] ?? match[5]);
  const episode = Number(match[2] ?? match[6]);
  const last = match[3] ?? match[4] ?? match[7];
  const end = last === undefined ? undefined : Number(last);
  // Only a short forward range is a multi-episode file (the server confirms at most 10 episodes).
  return end !== undefined && end > episode && end - episode < 10
    ? { season, episode, episodeEnd: end }
    : { season, episode };
}

function clean(value: string): string {
  return value
    .replace(/[._]/g, ' ')
    .replace(/[[\]()]/g, ' ')
    .replace(/\s+/g, ' ')
    .replace(/^[\s-]+|[\s-]+$/g, '');
}
