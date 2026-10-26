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
  WritableSignal,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowDown, faArrowUp, faTrashCan } from '@fortawesome/free-solid-svg-icons';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { PluginDescriptor, PluginInstance, PluginOptions, operationId } from '../../rename-api';
import { sampleEpisode, sampleMovie, tmdbTemplatePreview } from '../../tmdb-template-preview';

interface Choice {
  value: string;
  label: string;
}
interface OptionField {
  key: keyof PluginOptions;
  label: string;
  choices?: Choice[];
  checkbox?: boolean;
  hint?: string;
  placeholder?: string;
  list?: string;
  /** Hidden (not removed) unless all of these boolean options are on; its value is still emitted. */
  requires?: (keyof PluginOptions)[];
}
const scopeChoices: Choice[] = [
  { value: 'all', label: 'Every match' },
  { value: 'first', label: 'First match only' },
];
const caseChoices: Choice[] = [
  { value: 'upper', label: 'UPPERCASE' },
  { value: 'lower', label: 'lowercase' },
  { value: 'title', label: 'Title Case' },
];
const fields: Record<string, OptionField[]> = {
  literal: [
    { key: 'search', label: 'Find text' },
    { key: 'replacement', label: 'Replace with' },
    { key: 'scope', label: 'Replace', choices: scopeChoices },
    { key: 'ignoreCase', label: 'Ignore case', checkbox: true },
  ],
  regex: [
    { key: 'pattern', label: 'Pattern', hint: '.NET regular expression syntax' },
    {
      key: 'replacement',
      label: 'Replace with',
      hint: 'Use $1 or ${name} for captured groups',
    },
    { key: 'ignoreCase', label: 'Ignore case', checkbox: true },
  ],
  case: [{ key: 'mode', label: 'Case', choices: caseChoices }],
  tmdb: [
    {
      key: 'nameTemplate',
      label: 'Filename template',
      placeholder:
        'Default: {title} ({year}) for movies, {title} - S{season}E{episode} - {episodeTitle} for episodes',
      hint: 'Leave empty to use the default for each kind of file.',
    },
    {
      key: 'language',
      label: 'Metadata language',
      list: 'tmdb-languages',
      hint: 'Sets the language of titles. Pick one or type your own.',
    },
    { key: 'changeFolder', label: 'Allow folder change', checkbox: true },
    {
      key: 'baseFolder',
      label: 'Base folder',
      requires: ['changeFolder'],
      hint: 'Relative to the library root, with forward slashes, e.g. Movies/Archive. Leave empty to start at the root.',
    },
    {
      key: 'folderTemplate',
      label: 'Folder template',
      requires: ['changeFolder'],
      hint: 'Creates a folder inside the base folder.',
    },
    {
      key: 'seasonFolder',
      label: 'Add season folder for episodes',
      checkbox: true,
      requires: ['changeFolder'],
    },
    {
      key: 'seasonFolderTemplate',
      label: 'Season folder template',
      requires: ['changeFolder', 'seasonFolder'],
      hint: 'Season {season} gives Season 01, Season {seasonNumber} gives Season 1, S{season} gives S01. Movies ignore it.',
    },
  ],
};

const descriptions: Record<string, string> = {
  literal: 'Replace a piece of text',
  regex: 'Advanced pattern replace',
  case: 'UPPER, lower or Title Case',
  tmdb: 'Name movies and episodes from The Movie Database',
};
const fallbackDescription = 'Add this step to the recipe';
const locales = [
  'en-US',
  'en-GB',
  'es-ES',
  'ca-ES',
  'fr-FR',
  'de-DE',
  'it-IT',
  'pt-PT',
  'pt-BR',
  'ja-JP',
  'ko-KR',
  'zh-CN',
];

interface Card {
  plugin: PluginInstance;
  fields: OptionField[];
  form: FormGroup<Record<string, FormControl<string | boolean>>>;
  values: WritableSignal<Record<string, string | boolean>>;
}

@Component({
  imports: [FaIconComponent, ReactiveFormsModule],
  selector: 'app-plugin-editor',
  styleUrl: './plugin-editor.component.scss',
  templateUrl: './plugin-editor.component.html',
})
export class PluginEditorComponent {
  readonly catalog = input.required<PluginDescriptor[]>();
  readonly pluginsChanged = output<PluginInstance[]>();
  protected readonly maxPlugins = 32;
  protected readonly locales = locales;
  protected readonly upIcon = faArrowUp;
  protected readonly downIcon = faArrowDown;
  protected readonly trashIcon = faTrashCan;
  protected readonly cards = signal<Card[]>([]);
  protected readonly samples = [
    { label: 'Movie', match: sampleMovie },
    { label: 'Episode', match: sampleEpisode },
  ];
  protected readonly preview = tmdbTemplatePreview;
  private readonly injector = inject(Injector);
  private readonly headings = viewChildren<ElementRef<HTMLElement>>('cardHeading');
  private readonly firstAddButton = viewChild<ElementRef<HTMLButtonElement>>('addButton');

  protected add(descriptor: PluginDescriptor): void {
    if (this.cards().length >= this.maxPlugins) return;
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
    const values = signal<Record<string, string | boolean>>(form.getRawValue());
    const card: Card = { plugin, fields: optionFields, form, values };
    form.valueChanges.subscribe(() => {
      values.set(form.getRawValue());
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

  protected description(typeId: string): string {
    return descriptions[typeId] ?? fallbackDescription;
  }

  protected visible(card: Card, field: OptionField): boolean {
    const values = card.values();
    return (field.requires ?? []).every((key) => values[key] === true);
  }

  private emit(): void {
    this.pluginsChanged.emit(this.cards().map((card) => card.plugin));
  }

  private focusCard(index: number): void {
    afterNextRender(
      () => {
        const heading = this.headings()[index];
        (heading?.nativeElement ?? this.firstAddButton()?.nativeElement)?.focus();
      },
      { injector: this.injector },
    );
  }
}
