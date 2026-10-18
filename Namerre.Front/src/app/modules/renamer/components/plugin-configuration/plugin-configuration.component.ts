import { Component, input, output } from '@angular/core';
import { PluginDescriptor, PluginInstance } from '../../rename-api';
import { PluginEditorComponent } from '../plugin-editor/plugin-editor.component';

@Component({
  imports: [PluginEditorComponent],
  selector: 'app-plugin-configuration',
  styleUrl: './plugin-configuration.component.scss',
  templateUrl: './plugin-configuration.component.html',
})
export class PluginConfigurationComponent {
  readonly catalog = input<PluginDescriptor[]>([]);
  readonly loaded = input(false);
  readonly error = input('');
  readonly retryRequested = output<void>();
  readonly pluginsChanged = output<PluginInstance[]>();
}
