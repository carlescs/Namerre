import { operationId, type PluginInstance, type PluginOptions } from './rename-api';
import presets from './simple-presets.json';

/** One step of a built-in preset, as the server's plugin catalog reads it. */
export interface SimplePresetStep {
  typeId: string;
  version: number;
  options: PluginOptions;
}

/** A key option the Simple mode shows; it edits `option` of the step at index `step`. */
export interface SimplePresetField {
  key: string;
  label: string;
  hint?: string;
  kind: 'text' | 'select' | 'number';
  step: number;
  option: string;
  choices?: { value: string; label: string }[];
  suggestions?: string[];
  min?: number;
  max?: number;
}

/**
 * A built-in pipeline of the Simple mode. The data lives in `simple-presets.json`, which the server's
 * `SimplePresetTests` previews against the built-in plugin catalog.
 */
export interface SimplePreset {
  id: string;
  name: string;
  description: string;
  /** The search type proposed for TMDB matching when the preset has a TMDB step. */
  tmdbMediaType?: 'movie' | 'tv';
  steps: SimplePresetStep[];
  fields: SimplePresetField[];
}

export type SimplePresetValues = Record<string, string | number>;

export const simplePresets: readonly SimplePreset[] = presets as SimplePreset[];

/**
 * Run options of a Simple mode run: related files follow their video (the file selection's
 * default), colliding names are reported, files are checked by size and modification time and
 * folders left empty are removed — the defaults of the Advanced mode.
 */
export const simpleRunOptions = {
  verifyContent: false,
  deleteEmptyFolders: true,
  numberCollisions: false,
} as const;

/** The values a preset's fields start with: those of its steps. */
export function presetDefaults(preset: SimplePreset): SimplePresetValues {
  return Object.fromEntries(
    preset.fields.map((field) => [
      field.key,
      preset.steps[field.step].options[field.option] as string | number,
    ]),
  );
}

/**
 * The pipeline of a preset with the given field values. Steps keep the instance ids of `previous`
 * where the type matches, so confirmed TMDB matches survive a change that does not affect them.
 */
export function buildPipeline(
  preset: SimplePreset,
  values: SimplePresetValues,
  previous: readonly PluginInstance[] = [],
): PluginInstance[] {
  return preset.steps.map((step, index) => {
    const options: PluginOptions = structuredClone(step.options);
    for (const field of preset.fields) {
      if (field.step === index && field.key in values) options[field.option] = values[field.key];
    }
    const reused = previous[index]?.typeId === step.typeId ? previous[index].instanceId : '';
    return {
      instanceId: reused || operationId(),
      typeId: step.typeId,
      version: step.version,
      enabled: true,
      options,
    };
  });
}

/** Whether a field accepts the value: a listed choice, an integer in range or any text. */
export function validValue(field: SimplePresetField, value: unknown): value is string | number {
  switch (field.kind) {
    case 'select':
      return typeof value === 'string' && !!field.choices?.some((choice) => choice.value === value);
    case 'number':
      return (
        typeof value === 'number' &&
        Number.isInteger(value) &&
        value >= (field.min ?? Number.MIN_SAFE_INTEGER) &&
        value <= (field.max ?? Number.MAX_SAFE_INTEGER)
      );
    case 'text':
      return typeof value === 'string';
  }
}

/**
 * The preset that builds exactly these steps (with some field values), or null. Steps a preset
 * cannot express — another order, a disabled step, an "Apply to" filter or a changed option that
 * is not one of its fields — match none.
 */
export function matchSimplePreset(
  plugins: readonly PluginInstance[],
): { preset: SimplePreset; values: SimplePresetValues } | null {
  for (const preset of simplePresets) {
    if (preset.steps.length !== plugins.length) continue;
    const values: SimplePresetValues = {};
    const valid = preset.fields.every((field) => {
      const value = plugins[field.step].options[field.option];
      if (!validValue(field, value)) return false;
      values[field.key] = value;
      return true;
    });
    if (!valid) continue;
    const expected = buildPipeline(preset, values, plugins);
    const same = expected.every((step, index) => {
      const plugin = plugins[index];
      return (
        plugin.typeId === step.typeId &&
        plugin.version === step.version &&
        plugin.enabled &&
        !hasCondition(plugin) &&
        Object.entries(step.options).every(([key, value]) => equal(plugin.options[key], value))
      );
    });
    if (same) return { preset, values };
  }
  return null;
}

/** Whether the Simple mode can show these steps without discarding any: none, or a preset's. */
export function simpleCompatible(plugins: readonly PluginInstance[]): boolean {
  return !plugins.length || !!matchSimplePreset(plugins);
}

function hasCondition(plugin: PluginInstance): boolean {
  const condition = plugin.condition;
  return !!condition && (!!condition.extensions?.length || !!condition.pattern);
}

function equal(left: unknown, right: unknown): boolean {
  if (Array.isArray(left) && Array.isArray(right)) {
    return left.length === right.length && left.every((item, index) => equal(item, right[index]));
  }
  return left === right;
}
