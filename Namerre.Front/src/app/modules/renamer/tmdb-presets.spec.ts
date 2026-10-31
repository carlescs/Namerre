import { FormControl, FormGroup } from '@angular/forms';
import type { PluginOptions } from './rename-api';
import { customPreset, linkTmdbPreset, tmdbPresets } from './tmdb-presets';
import { sampleEpisode, sampleMovie, tmdbTemplatePreview } from './tmdb-template-preview';

describe('TMDB naming presets', () => {
  const expected: Record<string, string> = {
    plexMovies: 'Movies/Sample Movie (2024) {tmdb-12345}/Sample Movie (2024).mkv',
    plexTv:
      'TV/Sample Show (2019) {tmdb-67890}/Season 02/Sample Show (2019) - s02e05 - The Example Episode.mkv',
    jellyfinMovies: 'Movies/Sample Movie (2024) [tmdbid-12345]/Sample Movie (2024).mkv',
    jellyfinTv:
      'TV/Sample Show (2019) [tmdbid-67890]/Season 02/Sample Show S02E05 - The Example Episode.mkv',
    embyMovies: 'Movies/Sample Movie (2024) [tmdbid=12345]/Sample Movie (2024).mkv',
    embyTv:
      'TV/Sample Show (2019) [tmdbid=67890]/Season 2/Sample Show - S02E05 - The Example Episode.mkv',
    kodiMovies: 'Movies/Sample Movie (2024)/Sample Movie (2024).mkv',
    kodiTv: 'TV/Sample Show (2019)/Season 02/Sample Show S02E05 - The Example Episode.mkv',
  };

  it('renders each documented layout for its media type', () => {
    expect(Object.keys(tmdbPresets)).toEqual(Object.keys(expected));
    for (const [preset, options] of Object.entries(tmdbPresets)) {
      const tv = preset.endsWith('Tv');
      const result = tmdbTemplatePreview(
        { ...options, baseFolder: tv ? 'TV' : 'Movies' },
        tv ? sampleEpisode : sampleMovie,
      );
      expect(result).toEqual({ path: expected[preset] });
    }
  });

  it('fills the templates and switches back to Custom when one is edited', () => {
    const form = new FormGroup({
      preset: new FormControl(customPreset),
      nameTemplate: new FormControl(''),
      changeFolder: new FormControl(false),
      baseFolder: new FormControl('Library'),
      folderTemplate: new FormControl('{title} ({year})'),
      seasonFolder: new FormControl(false),
      seasonFolderTemplate: new FormControl('Season {season}'),
      language: new FormControl('fr-FR'),
    });
    linkTmdbPreset(form);

    form.controls.preset.setValue('jellyfinTv');
    expect(form.getRawValue() as PluginOptions).toEqual({
      ...tmdbPresets['jellyfinTv'],
      preset: 'jellyfinTv',
      baseFolder: 'Library',
      language: 'fr-FR',
    });

    form.controls.baseFolder.setValue('Shows');
    form.controls.language.setValue('de-DE');
    expect(form.controls.preset.value).toBe('jellyfinTv');
    form.controls.seasonFolderTemplate.setValue('S{season}');
    expect(form.controls.preset.value).toBe(customPreset);
    expect(form.controls.seasonFolderTemplate.value).toBe('S{season}');
  });

  it('leaves the templates alone when Custom is picked', () => {
    const form = new FormGroup({
      preset: new FormControl('plexMovies'),
      nameTemplate: new FormControl('{title}'),
    });
    linkTmdbPreset(form);
    form.controls.preset.setValue(customPreset);
    expect(form.controls.nameTemplate.value).toBe('{title}');
  });
});
