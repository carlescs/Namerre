import type { PluginInstance } from './rename-api';
import {
  buildPipeline,
  matchSimplePreset,
  presetDefaults,
  type SimplePreset,
  simpleCompatible,
  simplePresets,
} from './simple-presets';
import { tmdbPresets } from './tmdb-presets';

function preset(id: string): SimplePreset {
  const found = simplePresets.find((entry) => entry.id === id);
  if (!found) throw new Error(`No preset ${id}`);
  return found;
}

describe('simple presets', () => {
  it('lists the built-in presets with unique ids and valid field bindings', () => {
    expect(simplePresets.map((entry) => entry.id)).toEqual([
      'tvEpisodes',
      'movies',
      'cleanUp',
      'numberFiles',
    ]);
    for (const entry of simplePresets) {
      for (const field of entry.fields) {
        expect(entry.steps[field.step]?.options[field.option]).toBeDefined();
      }
      expect(matchSimplePreset(buildPipeline(entry, presetDefaults(entry)))?.preset).toBe(entry);
    }
  });

  it('recognises every choice of every select field', () => {
    for (const entry of simplePresets) {
      for (const field of entry.fields.filter((item) => item.kind === 'select')) {
        for (const choice of field.choices ?? []) {
          const values = { ...presetDefaults(entry), [field.key]: choice.value };
          expect(matchSimplePreset(buildPipeline(entry, values))).toEqual({
            preset: entry,
            values,
          });
        }
      }
    }
  });

  it('offers the episode and movie names of the media server layouts', () => {
    const templates = new Set(Object.values(tmdbPresets).map((layout) => layout.nameTemplate));
    const style = preset('tvEpisodes').fields.find((field) => field.key === 'style');
    for (const choice of style?.choices ?? []) expect(templates).toContain(choice.value);
    expect(templates).toContain(preset('movies').steps[0].options.nameTemplate);
  });

  it('builds enabled steps that keep the instance ids of the same types', () => {
    const tv = preset('tvEpisodes');
    const first = buildPipeline(tv, presetDefaults(tv));
    expect(first.map((step) => [step.typeId, step.enabled])).toEqual([
      ['tmdb', true],
      ['separators', true],
    ]);
    const next = buildPipeline(tv, { ...presetDefaults(tv), language: 'es-ES' }, first);
    expect(next.map((step) => step.instanceId)).toEqual(first.map((step) => step.instanceId));
    expect(next[0].options.language).toBe('es-ES');
    expect(tv.steps[0].options.language).toBe('en-US');
    const movies = buildPipeline(preset('movies'), presetDefaults(preset('movies')), first);
    expect(movies[0].instanceId).toBe(first[0].instanceId);
  });

  it('matches no preset once a step goes beyond it', () => {
    const counter = preset('numberFiles');
    const steps = buildPipeline(counter, presetDefaults(counter));
    const variants: PluginInstance[][] = [
      [{ ...steps[0], enabled: false }],
      [{ ...steps[0], condition: { extensions: ['mkv'], ignoreCase: true, invert: false } }],
      [{ ...steps[0], options: { ...steps[0].options, perFolder: true } }],
      [{ ...steps[0], options: { ...steps[0].options, position: 'template' } }],
      [{ ...steps[0], options: { ...steps[0].options, width: 11 } }],
      [{ ...steps[0], version: 2 }],
      [...steps, ...steps],
    ];
    for (const plugins of variants) {
      expect(matchSimplePreset(plugins)).toBeNull();
      expect(simpleCompatible(plugins)).toBe(false);
    }
    const clean = preset('cleanUp');
    const reordered = buildPipeline(clean, presetDefaults(clean)).reverse();
    expect(matchSimplePreset(reordered)).toBeNull();
  });

  it('accepts an empty condition and an empty pipeline', () => {
    const counter = preset('numberFiles');
    const [step] = buildPipeline(counter, { ...presetDefaults(counter), start: 5 });
    expect(matchSimplePreset([{ ...step, condition: null }])?.values['start']).toBe(5);
    expect(simpleCompatible([])).toBe(true);
  });
});
