import {
  afterNextRender,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { PluginDescriptor, PluginInstance, PluginOptions, operationId } from '../../rename-api';
import { sampleEpisode, sampleMovie, tmdbTemplatePreview } from '../../tmdb-template-preview';

interface OptionField {
  key: keyof PluginOptions;
  label: string;
  choices?: string[];
  checkbox?: boolean;
}
const fields: Record<string, OptionField[]> = {
  literal: [
    { key: 'search', label: 'Find text' },
    { key: 'replacement', label: 'Replace with' },
    { key: 'ignoreCase', label: 'Ignore case', checkbox: true },
    { key: 'scope', label: 'Replacement scope', choices: ['all', 'first'] },
  ],
  regex: [
    { key: 'pattern', label: '.NET regular expression' },
    { key: 'replacement', label: 'Replacement ($1 or ${name} captures)' },
    { key: 'ignoreCase', label: 'Ignore case', checkbox: true },
  ],
  case: [{ key: 'mode', label: 'Case mode', choices: ['upper', 'lower', 'title'] }],
  tmdb: [
    { key: 'nameTemplate', label: 'Filename stem template' },
    { key: 'language', label: 'Metadata language' },
    { key: 'changeFolder', label: 'Allow folder change', checkbox: true },
    { key: 'baseFolder', label: 'Base folder (only used when folder change is enabled)' },
    { key: 'folderTemplate', label: 'Folder template (only used when enabled)' },
    { key: 'seasonFolder', label: 'Add season folder for episodes', checkbox: true },
    { key: 'seasonFolderTemplate', label: 'Season folder template (only used when enabled)' },
  ],
};

interface Card {
  plugin: PluginInstance;
  fields: OptionField[];
  form: FormGroup<Record<string, FormControl<string | boolean>>>;
}

@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-plugin-editor',
  styleUrl: './plugin-editor.component.scss',
  templateUrl: './plugin-editor.component.html',
})
export class PluginEditorComponent {
  readonly catalog = input.required<PluginDescriptor[]>();
  readonly pluginsChanged = output<PluginInstance[]>();
  protected readonly type = new FormControl('', { nonNullable: true });
  protected readonly cards = signal<Card[]>([]);
  protected readonly samples = [
    { label: 'Movie', match: sampleMovie },
    { label: 'Episode', match: sampleEpisode },
  ];
  protected readonly preview = tmdbTemplatePreview;
  private readonly injector = inject(Injector);
  private readonly headings = viewChildren<ElementRef<HTMLElement>>('cardHeading');
  private readonly addButton = viewChild.required<ElementRef<HTMLButtonElement>>('addButton');

  protected add(): void {
    const descriptor = this.catalog().find((plugin) => plugin.typeId === this.type.value);
    if (!descriptor || this.cards().length >= 32) return;
    const plugin: PluginInstance = {
      instanceId: operationId(),
      typeId: descriptor.typeId,
      version: descriptor.version,
      enabled: true,
      options: { ...descriptor.defaultOptions },
    };
    const optionFields = fields[plugin.typeId] ?? [];
    const controls: Record<string, FormControl<string | boolean>> = {};
    for (const field of optionFields) {
      controls[field.key] = new FormControl<string | boolean>(
        plugin.options[field.key] ?? (field.checkbox ? false : ''),
        { nonNullable: true },
      );
    }
    const form = new FormGroup(controls);
    const card: Card = { plugin, fields: optionFields, form };
    form.valueChanges.subscribe(() => {
      card.plugin = { ...card.plugin, options: form.getRawValue() as PluginOptions };
      this.emit();
    });
    this.cards.update((cards) => [...cards, card]);
    this.emit();
    this.focusCard(this.cards().length - 1);
  }

  protected enable(index: number): void {
    const cards = [...this.cards()];
    cards[index].plugin = { ...cards[index].plugin, enabled: !cards[index].plugin.enabled };
    this.cards.set(cards);
    this.emit();
  }

  protected move(index: number, direction: number): void {
    const cards = [...this.cards()];
    const destination = index + direction;
    if (destination < 0 || destination >= cards.length) return;
    [cards[index], cards[destination]] = [cards[destination], cards[index]];
    this.cards.set(cards);
    this.emit();
    this.focusCard(destination);
  }

  protected remove(index: number): void {
    this.cards.update((cards) => cards.filter((_, i) => i !== index));
    this.emit();
    this.focusCard(Math.min(index, this.cards().length - 1));
  }

  protected name(typeId: string): string {
    return this.catalog().find((plugin) => plugin.typeId === typeId)?.name ?? typeId;
  }

  private emit(): void {
    this.pluginsChanged.emit(this.cards().map((card) => card.plugin));
  }

  private focusCard(index: number): void {
    afterNextRender(
      () => {
        const heading = this.headings()[index];
        (heading?.nativeElement ?? this.addButton().nativeElement).focus();
      },
      { injector: this.injector },
    );
  }
}
