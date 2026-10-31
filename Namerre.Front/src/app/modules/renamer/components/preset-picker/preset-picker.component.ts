import { Component, computed, input, output } from '@angular/core';
import type { PluginInstance } from '../../rename-api';
import {
  buildPipeline,
  matchSimplePreset,
  presetDefaults,
  type SimplePreset,
  type SimplePresetField,
  simplePresets,
  validValue,
} from '../../simple-presets';

/** Simple mode's step: one card per built-in preset and a few of its key options. */
@Component({
  selector: 'app-preset-picker',
  styleUrl: './preset-picker.component.scss',
  templateUrl: './preset-picker.component.html',
})
export class PresetPickerComponent {
  /** The current steps; the preset that builds them is shown as chosen. */
  readonly plugins = input<PluginInstance[]>([]);
  readonly disabled = input(false);
  readonly pluginsChanged = output<PluginInstance[]>();
  protected readonly presets = simplePresets;
  protected readonly match = computed(() => matchSimplePreset(this.plugins()));
  /** Steps built in Advanced mode that no preset matches; choosing a preset replaces them. */
  protected readonly unsupported = computed(() => this.plugins().length > 0 && !this.match());

  protected choose(preset: SimplePreset): void {
    if (this.disabled() || this.match()?.preset.id === preset.id) return;
    const values = presetDefaults(preset);
    // Carry shared settings, such as the metadata language, from the preset chosen before.
    const previous = this.match()?.values ?? {};
    for (const field of preset.fields) {
      if (validValue(field, previous[field.key])) values[field.key] = previous[field.key];
    }
    this.pluginsChanged.emit(buildPipeline(preset, values, this.plugins()));
  }

  protected value(field: SimplePresetField): string | number {
    return this.match()?.values[field.key] ?? '';
  }

  protected change(field: SimplePresetField, event: Event): void {
    const element = event.target as HTMLInputElement | HTMLSelectElement;
    const current = this.match();
    if (!current) return;
    const value = this.parse(field, element.value, current.values[field.key]);
    // Show the accepted value, e.g. a number brought into range or a restored blank language.
    element.value = String(value);
    if (value === current.values[field.key]) return;
    this.pluginsChanged.emit(
      buildPipeline(current.preset, { ...current.values, [field.key]: value }, this.plugins()),
    );
  }

  protected fieldId(field: SimplePresetField): string {
    return `simple-${this.match()?.preset.id}-${field.key}`;
  }

  private parse(
    field: SimplePresetField,
    text: string,
    fallback: string | number,
  ): string | number {
    switch (field.kind) {
      case 'number': {
        const number = Math.round(Number(text));
        if (text.trim() === '' || !Number.isFinite(number)) return fallback;
        return Math.min(field.max ?? number, Math.max(field.min ?? number, number));
      }
      case 'select':
        return validValue(field, text) ? text : fallback;
      case 'text':
        return text.trim() || fallback;
    }
  }
}
