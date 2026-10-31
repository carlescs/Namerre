// Related files that follow a video: subtitles, the .nfo and -poster/-fanart/-thumb artwork in the
// same folder, named after the video's stem. Mirrors Namerre.Application/Renaming/CompanionFiles.cs,
// which decides on the server; keep both in sync.

const videoExtensions = new Set([
  '.3gp',
  '.avi',
  '.divx',
  '.flv',
  '.m2ts',
  '.m4v',
  '.mkv',
  '.mov',
  '.mp4',
  '.mpeg',
  '.mpg',
  '.mts',
  '.ogm',
  '.ogv',
  '.ts',
  '.vob',
  '.webm',
  '.wmv',
]);
const subtitleExtensions = new Set(['.srt', '.ass', '.ssa', '.sub', '.idx', '.vtt']);
const imageExtensions = new Set(['.jpg', '.jpeg', '.png', '.webp', '.tbn']);
const artworkKinds = new Set(['-poster', '-fanart', '-thumb']);
const subtitleFlags = new Set(['hi', 'sdh', 'cc', 'forced', 'default']);
// ISO 639-1 codes; three-letter codes are accepted in lower case only to limit false positives.
const iso6391 = new Set(
  (
    'aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz ' +
    'ee el en eo es et eu fa ff fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik ' +
    'io is it iu ja jv ka kg ki kj kk kl km kn ko kr ks ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml ' +
    'mn mr ms mt my na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps pt qu rm rn ro ru rw sa sc sd ' +
    'se sg si sk sl sm sn so sq sr ss st su sv sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi ' +
    'vo wa wo xh yi yo za zh zu'
  ).split(' '),
);

export function isVideo(name: string): boolean {
  return videoExtensions.has(extensionOf(name).toLowerCase());
}

/**
 * The part of `name` after `videoStem` when the file is one of the video's companions (such as
 * `.en.srt`, `.nfo` or `-poster.jpg`), otherwise null. `Movie 2.srt` never follows `Movie.mkv`.
 */
export function companionSuffix(videoStem: string, name: string): string | null {
  if (!videoStem || name.length <= videoStem.length || !name.startsWith(videoStem)) return null;
  const rest = name.slice(videoStem.length);
  if (rest.toLowerCase() === '.nfo') return rest;
  const extension = extensionOf(rest);
  const lower = extension.toLowerCase();
  const middle = rest.slice(0, rest.length - extension.length);
  if (imageExtensions.has(lower) && artworkKinds.has(middle.toLowerCase())) return rest;
  if (!subtitleExtensions.has(lower)) return null;
  if (!middle) return rest;
  return middle.startsWith('.') && middle.slice(1).split('.').every(isSubtitleToken) ? rest : null;
}

/**
 * Maps each companion path to the path of the video it follows, among `paths` and within the same
 * folder, preferring the longest matching video stem.
 */
export function detectCompanions(paths: readonly string[]): Map<string, string> {
  const videos = paths
    .filter((path) => isVideo(nameOf(path)))
    .map((path) => ({ path, folder: folderOf(path), stem: stemOf(nameOf(path)) }));
  const result = new Map<string, string>();
  if (!videos.length) return result;
  for (const path of paths) {
    const name = nameOf(path);
    if (isVideo(name)) continue;
    const folder = folderOf(path);
    let best: { path: string; length: number } | null = null;
    for (const video of videos) {
      if (video.folder !== folder || video.stem.length <= (best?.length ?? -1)) continue;
      if (companionSuffix(video.stem, name) !== null) {
        best = { path: video.path, length: video.stem.length };
      }
    }
    if (best) result.set(path, best.path);
  }
  return result;
}

function isSubtitleToken(token: string): boolean {
  if (subtitleFlags.has(token.toLowerCase())) return true;
  const separator = token.search(/[-_]/);
  const language = separator < 0 ? token : token.slice(0, separator);
  if (separator >= 0 && !/^([A-Za-z]{2}|[A-Za-z]{4})$/.test(token.slice(separator + 1))) {
    return false;
  }
  return language.length === 2 ? iso6391.has(language.toLowerCase()) : /^[a-z]{3}$/.test(language);
}

function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot < 0 || dot === name.length - 1 ? '' : name.slice(dot);
}

function stemOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot < 0 ? name : name.slice(0, dot);
}

function nameOf(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}

function folderOf(path: string): string {
  return path.slice(0, path.lastIndexOf('/') + 1);
}
