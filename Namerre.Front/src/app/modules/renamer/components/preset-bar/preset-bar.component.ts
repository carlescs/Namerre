import { Component, DestroyRef, inject, input, type OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import type { Observable } from 'rxjs';
import {
  type PluginInstance,
  type PresetSummary,
  type PresetView,
  RenameApi,
  renameError,
  type SavePresetRequest,
} from '../../rename-api';

/** Run options a preset keeps next to its steps. */
export interface PresetRunOptions {
  verifyContent: boolean;
  deleteEmptyFolders: boolean;
  numberCollisions: boolean;
}

/** Most characters in a preset name (`PresetService.MaxNameLength` on the server). */
const MAX_NAME_LENGTH = 100;

/** Saves the current recipe as a named preset on the server and loads one back. */
@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-preset-bar',
  templateUrl: './preset-bar.component.html',
})
export class PresetBarComponent implements OnInit {
  readonly plugins = input.required<PluginInstance[]>();
  readonly options = input.required<PresetRunOptions>();
  /** A preset was loaded; it replaces the draft. */
  readonly loaded = output<PresetView>();
  private readonly api = inject(RenameApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly maxNameLength = MAX_NAME_LENGTH;
  protected readonly presets = signal<PresetSummary[]>([]);
  protected readonly selected = new FormControl('', { nonNullable: true });
  protected readonly name = new FormControl('', {
    nonNullable: true,
    validators: [
      Validators.required,
      Validators.maxLength(MAX_NAME_LENGTH),
      Validators.pattern(/\S/),
    ],
  });
  /** The preset last loaded or saved; "Save changes" overwrites it. */
  protected readonly active = signal<PresetSummary | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmingDelete = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');

  ngOnInit(): void {
    this.refresh();
  }

  protected load(): void {
    const id = this.selected.value;
    if (!id) return;
    this.run(this.api.preset(id), (preset) => {
      this.remember(preset);
      this.loaded.emit(preset);
      this.message.set(
        preset.unknownSteps.length
          ? `Loaded ${preset.name}. Some steps need attention: this server does not have their plugin.`
          : `Loaded ${preset.name}.`,
      );
    });
  }

  protected saveNew(): void {
    if (this.name.invalid) return;
    this.run(this.api.createPreset(this.request()), (preset) => {
      this.remember(preset);
      this.message.set(`Saved ${preset.name}.`);
      this.refresh();
    });
  }

  protected saveChanges(): void {
    const active = this.active();
    if (!active || this.name.invalid) return;
    this.run(this.api.updatePreset(active.id, this.request()), (preset) => {
      this.remember(preset);
      this.message.set(`Saved changes to ${preset.name}.`);
      this.refresh();
    });
  }

  protected rename(): void {
    const active = this.active();
    if (!active || this.name.invalid) return;
    this.run(this.api.renamePreset(active.id, this.name.value), (preset) => {
      this.remember(preset);
      this.message.set(`Renamed to ${preset.name}.`);
      this.refresh();
    });
  }

  protected remove(): void {
    const id = this.selected.value;
    if (!id) return;
    if (!this.confirmingDelete()) {
      this.confirmingDelete.set(true);
      return;
    }
    const name = this.presets().find((preset) => preset.id === id)?.name ?? 'the preset';
    this.run(this.api.deletePreset(id), () => {
      if (this.active()?.id === id) this.active.set(null);
      this.selected.setValue('');
      this.message.set(`Deleted ${name}.`);
      this.refresh();
    });
  }

  protected cancelDelete(): void {
    this.confirmingDelete.set(false);
  }

  private request(): SavePresetRequest {
    const options = this.options();
    return {
      name: this.name.value.trim(),
      plugins: this.plugins(),
      verifyContent: options.verifyContent,
      deleteEmptyFolders: options.deleteEmptyFolders,
      onCollision: options.numberCollisions ? 'AddNumber' : 'Report',
    };
  }

  private remember(preset: PresetView): void {
    this.active.set({
      id: preset.id,
      name: preset.name,
      steps: preset.plugins.length,
      updatedAt: preset.updatedAt,
    });
    this.selected.setValue(preset.id);
    this.name.setValue(preset.name);
  }

  private refresh(): void {
    this.api
      .presets()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (presets) => this.presets.set(presets),
        error: (error: unknown) => this.error.set(renameError(error)),
      });
  }

  private run<T>(request: Observable<T>, done: (value: T) => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.confirmingDelete.set(false);
    this.message.set('');
    this.error.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (value) => {
        this.busy.set(false);
        done(value);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(renameError(error));
      },
    });
  }
}
