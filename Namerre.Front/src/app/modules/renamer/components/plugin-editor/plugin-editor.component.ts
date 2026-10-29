import {
  afterNextRender,
  Component,
  type ElementRef,
  Injector,
  inject,
  input,
  type OnInit,
  output,
  signal,
  viewChild,
  viewChildren,
  type WritableSignal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowDown, faArrowUp, faTrashCan } from '@fortawesome/free-solid-svg-icons';
import {
  type OptionGroup,
  operationId,
  type PluginDescriptor,
  type PluginInstance,
  type PluginOptions,
} from '../../rename-api';
import { sampleEpisode, sampleMovie, tmdbTemplatePreview } from '../../tmdb-template-preview';

interface Card {
  plugin: PluginInstance;
  groups: OptionGroup[];
  form: FormGroup<Record<string, FormControl<string | boolean>>>;
  values: WritableSignal<Record<string, string | boolean>>;
}

@Component({
  imports: [FaIconComponent, ReactiveFormsModule],
  selector: 'app-plugin-editor',
  styleUrl: './plugin-editor.component.scss',
  templateUrl: './plugin-editor.component.html',
})
export class PluginEditorComponent implements OnInit {
  readonly catalog = input.required<PluginDescriptor[]>();
  /** Plugins restored from a saved draft; read once when the editor is created. */
  readonly initialPlugins = input<PluginInstance[]>([]);
  readonly pluginsChanged = output<PluginInstance[]>();
  protected readonly maxPlugins = 32;
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

  ngOnInit(): void {
    const single = new Set<string>();
    const restored = this.initialPlugins().filter((plugin) => {
      const descriptor = this.descriptor(plugin);
      if (!descriptor?.singleInstance) return !!descriptor;
      // Drafts saved before a type became single-instance keep only its first step.
      if (single.has(plugin.typeId)) return false;
      single.add(plugin.typeId);
      return true;
    });
    if (!restored.length) {
      if (this.initialPlugins().length) this.emit();
      return;
    }
    this.cards.set(restored.slice(0, this.maxPlugins).map((plugin) => this.card(plugin)));
    if (restored.length !== this.initialPlugins().length) this.emit();
  }

  protected canAdd(descriptor: PluginDescriptor): boolean {
    return (
      this.cards().length < this.maxPlugins &&
      !(descriptor.singleInstance && this.included(descriptor))
    );
  }

  /** Whether the recipe already has a step of this type. */
  protected included(descriptor: PluginDescriptor): boolean {
    return this.cards().some((card) => card.plugin.typeId === descriptor.typeId);
  }

  protected add(descriptor: PluginDescriptor): void {
    if (!this.canAdd(descriptor)) return;
    const card = this.card({
      instanceId: operationId(),
      typeId: descriptor.typeId,
      version: descriptor.version,
      enabled: true,
      options: { ...descriptor.defaultOptions },
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

  protected visible(card: Card, group: OptionGroup): boolean {
    const values = card.values();
    return (group.requires ?? []).every((key) => values[key] === true);
  }

  /** Lines a revealed group up under the text of the checkbox that revealed it. */
  protected indent(group: OptionGroup): number {
    const depth = group.requires?.length ?? 0;
    return depth ? 0.5 + (depth - 1) * 1.625 : 0;
  }

  private card(plugin: PluginInstance): Card {
    const optionGroups = this.descriptor(plugin)?.form ?? [];
    const controls: Record<string, FormControl<string | boolean>> = {};
    for (const field of optionGroups.flatMap((group) => group.fields)) {
      controls[field.key] = new FormControl<string | boolean>(
        plugin.options[field.key] ?? (field.kind === 'checkbox' ? false : ''),
        { nonNullable: true },
      );
    }
    const form = new FormGroup(controls);
    const values = signal<Record<string, string | boolean>>(form.getRawValue());
    const card: Card = { plugin, groups: optionGroups, form, values };
    form.valueChanges.subscribe(() => {
      values.set(form.getRawValue());
      card.plugin = { ...card.plugin, options: form.getRawValue() as PluginOptions };
      this.emit();
    });
    return card;
  }

  private descriptor(plugin: PluginInstance): PluginDescriptor | undefined {
    return this.catalog().find(
      (descriptor) => descriptor.typeId === plugin.typeId && descriptor.version === plugin.version,
    );
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
