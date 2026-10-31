import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  Component,
  computed,
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
import {
  type AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidatorFn,
  Validators,
} from '@angular/forms';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowDown, faArrowUp, faTrashCan } from '@fortawesome/free-solid-svg-icons';
import { WorkloadLimits } from '../../../../workload-limits';
import {
  type OptionField,
  type OptionGroup,
  operationId,
  type PluginDescriptor,
  type PluginInstance,
  type PluginOptionCell,
  type PluginOptionRow,
  type PluginOptions,
} from '../../rename-api';
import { linkTmdbPreset } from '../../tmdb-presets';
import { sampleEpisode, sampleMovie, tmdbTemplatePreview } from '../../tmdb-template-preview';

type OptionControl = FormControl<PluginOptionCell>;
type OptionRows = FormArray<FormGroup<Record<string, OptionControl>>>;

interface Card {
  plugin: PluginInstance;
  groups: OptionGroup[];
  /** One control per field; `list` fields hold a `FormArray` of row groups. */
  form: FormGroup<Record<string, OptionControl | OptionRows>>;
  values: WritableSignal<PluginOptions>;
}

/** Field kinds that always span the whole row. */
const wideKinds: ReadonlySet<OptionField['kind']> = new Set([
  'checkbox',
  'textarea',
  'list',
  'multiSelect',
]);

@Component({
  imports: [FaIconComponent, NgTemplateOutlet, ReactiveFormsModule],
  selector: 'app-plugin-editor',
  styleUrl: './plugin-editor.component.scss',
  templateUrl: './plugin-editor.component.html',
})
export class PluginEditorComponent implements OnInit {
  readonly catalog = input.required<PluginDescriptor[]>();
  /** Plugins restored from a saved draft; read once when the editor is created. */
  readonly initialPlugins = input<PluginInstance[]>([]);
  readonly pluginsChanged = output<PluginInstance[]>();
  private readonly limits = inject(WorkloadLimits);
  protected readonly maxPlugins = computed(() => this.limits.current().maxStepsPerRun);
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
    this.cards.set(restored.slice(0, this.maxPlugins()).map((plugin) => this.card(plugin)));
    if (restored.length !== this.initialPlugins().length) this.emit();
  }

  protected canAdd(descriptor: PluginDescriptor): boolean {
    return (
      this.cards().length < this.maxPlugins() &&
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

  /** Whether a field's `visibleWhen` condition, if any, holds for the card's current values. */
  protected shown(card: Card, field: OptionField): boolean {
    const condition = field.visibleWhen;
    if (!condition) return true;
    const value = card.values()[condition.key];
    if (Array.isArray(value))
      return value.some((item) => condition.values.includes(item as string));
    return typeof value === 'string' && condition.values.includes(value);
  }

  /** Lines a revealed group up under the text of the checkbox that revealed it. */
  protected indent(group: OptionGroup): number {
    const depth = group.requires?.length ?? 0;
    return depth ? 0.5 + (depth - 1) * 1.625 : 0;
  }

  protected wide(field: OptionField): boolean {
    return !!field.wide || wideKinds.has(field.kind);
  }

  protected control(card: Card, key: string): OptionControl {
    return card.form.controls[key] as OptionControl;
  }

  protected rows(card: Card, key: string): OptionRows {
    return card.form.controls[key] as OptionRows;
  }

  protected addRow(card: Card, field: OptionField): void {
    const rows = this.rows(card, field.key);
    rows.push(this.row(field, {}));
    this.focusRow(card, field, rows.length - 1);
  }

  protected moveRow(card: Card, field: OptionField, index: number, direction: number): void {
    const rows = this.rows(card, field.key);
    const destination = index + direction;
    if (destination < 0 || destination >= rows.length) return;
    const row = rows.at(index);
    rows.removeAt(index, { emitEvent: false });
    rows.insert(destination, row);
    this.focusRow(card, field, destination);
  }

  protected removeRow(card: Card, field: OptionField, index: number): void {
    this.rows(card, field.key).removeAt(index);
  }

  /** Whether a `multiSelect` control includes `value`. */
  protected selected(control: OptionControl, value: string): boolean {
    return Array.isArray(control.value) && control.value.includes(value);
  }

  /** Adds or removes one `multiSelect` choice, keeping the descriptor's choice order. */
  protected toggle(control: OptionControl, field: OptionField, value: string, on: boolean): void {
    const current = Array.isArray(control.value) ? control.value : [];
    control.setValue(
      (field.choices ?? [])
        .map((choice) => choice.value)
        .filter((choice) => (choice === value ? on : current.includes(choice))),
    );
    control.markAsDirty();
  }

  /** Validation message of a field, or null while its value is acceptable. */
  protected error(field: OptionField, control: OptionControl): string | null {
    const errors = control.errors;
    if (!errors) return null;
    if ('required' in errors) return 'Enter a number.';
    if ('min' in errors) return `Use ${field.min} or more.`;
    if ('max' in errors) return `Use ${field.max} or less.`;
    if ('step' in errors) {
      return field.step === 1 && Number.isInteger(field.min ?? 0)
        ? 'Use a whole number.'
        : `Use steps of ${field.step}.`;
    }
    return 'Check this value.';
  }

  protected describedBy(field: OptionField, control: OptionControl, id: string): string | null {
    const ids = [field.hint ? id : '', this.error(field, control) ? `${id}-error` : ''];
    return ids.filter(Boolean).join(' ') || null;
  }

  private card(plugin: PluginInstance): Card {
    const descriptor = this.descriptor(plugin);
    const optionGroups = descriptor?.form ?? [];
    const controls: Record<string, OptionControl | OptionRows> = {};
    for (const field of optionGroups.flatMap((group) => group.fields)) {
      const fallback = descriptor?.defaultOptions[field.key];
      controls[field.key] =
        field.kind === 'list'
          ? new FormArray(
              rowsOf(plugin.options[field.key] ?? fallback).map((row) => this.row(field, row)),
            )
          : optionControl(field, plugin.options[field.key], fallback);
    }
    const form = new FormGroup(controls);
    if (plugin.typeId === 'tmdb') linkTmdbPreset(form);
    const values = signal<PluginOptions>(form.getRawValue() as PluginOptions);
    const card: Card = { plugin, groups: optionGroups, form, values };
    form.valueChanges.subscribe(() => {
      values.set(form.getRawValue() as PluginOptions);
      card.plugin = { ...card.plugin, options: form.getRawValue() as PluginOptions };
      this.emit();
    });
    return card;
  }

  private row(field: OptionField, row: PluginOptionRow): FormGroup<Record<string, OptionControl>> {
    const controls: Record<string, OptionControl> = {};
    for (const column of field.fields ?? []) {
      controls[column.key] = optionControl(column, row[column.key], undefined);
    }
    return new FormGroup(controls);
  }

  private descriptor(plugin: PluginInstance): PluginDescriptor | undefined {
    return this.catalog().find(
      (descriptor) => descriptor.typeId === plugin.typeId && descriptor.version === plugin.version,
    );
  }

  private emit(): void {
    this.pluginsChanged.emit(this.cards().map((card) => card.plugin));
  }

  private focusRow(card: Card, field: OptionField, index: number): void {
    afterNextRender(
      () =>
        document
          .getElementById(`f-${card.plugin.instanceId}-${field.key}-${index}`)
          ?.querySelector<HTMLElement>('input, select, textarea')
          ?.focus(),
      { injector: this.injector },
    );
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

function optionControl(field: OptionField, value: unknown, fallback: unknown): OptionControl {
  return new FormControl<PluginOptionCell>(initialValue(field, value, fallback), {
    nonNullable: true,
    validators: validators(field),
  });
}

/** The saved value when it has the field's shape, else the descriptor default, else an empty value. */
function initialValue(field: OptionField, value: unknown, fallback: unknown): PluginOptionCell {
  for (const candidate of [value, fallback]) {
    switch (field.kind) {
      case 'checkbox':
        if (typeof candidate === 'boolean') return candidate;
        break;
      case 'number':
        if (typeof candidate === 'number' && Number.isFinite(candidate)) return candidate;
        break;
      case 'multiSelect':
        if (Array.isArray(candidate)) {
          return (field.choices ?? [])
            .map((choice) => choice.value)
            .filter((choice) => candidate.includes(choice));
        }
        break;
      default:
        if (typeof candidate === 'string') return candidate;
    }
  }
  switch (field.kind) {
    case 'checkbox':
      return false;
    case 'number':
      return Math.min(Math.max(0, field.min ?? 0), field.max ?? Number.POSITIVE_INFINITY);
    case 'multiSelect':
      return [];
    case 'select':
      return field.choices?.[0]?.value ?? '';
    default:
      return '';
  }
}

function rowsOf(value: unknown): PluginOptionRow[] {
  return Array.isArray(value)
    ? value.filter(
        (row): row is PluginOptionRow => !!row && typeof row === 'object' && !Array.isArray(row),
      )
    : [];
}

/** Mirrors the server-side checks of `OptionBinding` for `number` fields. */
function validators(field: OptionField): ValidatorFn[] {
  if (field.kind !== 'number') return [];
  const result = [Validators.required];
  if (field.min !== undefined) result.push(Validators.min(field.min));
  if (field.max !== undefined) result.push(Validators.max(field.max));
  const step = field.step;
  if (step) {
    const base = field.min ?? 0;
    result.push((control: AbstractControl) => {
      if (typeof control.value !== 'number') return null;
      const steps = (control.value - base) / step;
      return Math.abs(steps - Math.round(steps)) > 1e-9 ? { step: { step } } : null;
    });
  }
  return result;
}
