import type { FormGroup } from '@angular/forms';
import type { PluginOptions } from './rename-api';

/** The `preset` value of hand-written templates (`TmdbPlugin.CustomPreset` on the server). */
export const customPreset = 'custom';

type PresetOptions = Required<
  Pick<
    PluginOptions,
    'nameTemplate' | 'changeFolder' | 'folderTemplate' | 'seasonFolder' | 'seasonFolderTemplate'
  >
>;

const movie = (folderTemplate: string): PresetOptions => ({
  nameTemplate: '{title} ({year})',
  changeFolder: true,
  folderTemplate,
  seasonFolder: false,
  seasonFolderTemplate: 'Season {season}',
});

const show = (
  nameTemplate: string,
  folderTemplate: string,
  seasonFolderTemplate = 'Season {season}',
): PresetOptions => ({
  nameTemplate,
  changeFolder: true,
  folderTemplate,
  seasonFolder: true,
  seasonFolderTemplate,
});

/**
 * Layouts recommended by each media server. The base folder is the user's own library layout and
 * is left alone. Ids are the TMDB movie or show id, in the form each server reads.
 */
export const tmdbPresets: Readonly<Record<string, PresetOptions>> = {
  plexMovies: movie('{title} ({year}) {{tmdb-{tmdbId}}}'),
  plexTv: show(
    '{title} ({showYear}) - s{season}e{episode} - {episodeTitle}',
    '{title} ({showYear}) {{tmdb-{tmdbId}}}',
  ),
  jellyfinMovies: movie('{title} ({year}) [tmdbid-{tmdbId}]'),
  jellyfinTv: show(
    '{title} S{season}E{episode} - {episodeTitle}',
    '{title} ({showYear}) [tmdbid-{tmdbId}]',
  ),
  embyMovies: movie('{title} ({year}) [tmdbid={tmdbId}]'),
  embyTv: show(
    '{title} - S{season}E{episode} - {episodeTitle}',
    '{title} ({showYear}) [tmdbid={tmdbId}]',
    'Season {seasonNumber}',
  ),
  kodiMovies: movie('{title} ({year})'),
  kodiTv: show('{title} S{season}E{episode} - {episodeTitle}', '{title} ({showYear})'),
};

const presetFields = Object.keys(tmdbPresets['plexMovies']) as (keyof PresetOptions)[];

/**
 * Picking a preset fills in the templates and folder options; editing any of them afterwards
 * switches the preset back to Custom. The base folder and language are not part of a preset.
 */
export function linkTmdbPreset(form: FormGroup): void {
  const preset = form.controls['preset'];
  if (!preset) return;
  let applying = false;
  preset.valueChanges.subscribe((value) => {
    const options = typeof value === 'string' ? tmdbPresets[value] : undefined;
    if (!options) return;
    applying = true;
    try {
      form.patchValue(options);
    } finally {
      applying = false;
    }
  });
  for (const key of presetFields) {
    form.controls[key]?.valueChanges.subscribe(() => {
      if (!applying && preset.value !== customPreset) preset.setValue(customPreset);
    });
  }
}
