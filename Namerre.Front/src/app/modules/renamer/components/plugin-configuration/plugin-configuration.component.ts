import { Component, input, output, viewChild } from '@angular/core';
import type { PluginDescriptor, PluginInstance, PresetView } from '../../rename-api';
import { PluginEditorComponent } from '../plugin-editor/plugin-editor.component';
import { PresetBarComponent, type PresetRunOptions } from '../preset-bar/preset-bar.component';

@Component({
  imports: [PluginEditorComponent, PresetBarComponent],
  selector: 'app-plugin-configuration',
  styleUrl: './plugin-configuration.component.scss',
  templateUrl: './plugin-configuration.component.html',
})
export class PluginConfigurationComponent {
  readonly catalog = input<PluginDescriptor[]>([]);
  readonly loaded = input(false);
  readonly error = input('');
  readonly initialPlugins = input<PluginInstance[]>([]);
  /** The current steps and run options, saved by the preset bar. */
  readonly plugins = input<PluginInstance[]>([]);
  readonly runOptions = input.required<PresetRunOptions>();
  readonly retryRequested = output<void>();
  readonly pluginsChanged = output<PluginInstance[]>();
  /** A preset replaced the steps; the workflow takes over its run options. */
  readonly presetLoaded = output<PresetView>();
  private readonly editor = viewChild(PluginEditorComponent);

  protected loadPreset(preset: PresetView): void {
    this.editor()?.load(preset.plugins);
    this.presetLoaded.emit(preset);
  }
}
