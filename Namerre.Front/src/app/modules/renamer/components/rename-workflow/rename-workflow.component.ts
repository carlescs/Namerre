import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { exhaustMap, Subscription, timer } from 'rxjs';
import {
  MatchContext,
  MetadataSelection,
  PipelineRequest,
  PlanView,
  PluginDescriptor,
  PluginInstance,
  PreviewResult,
  RenameApi,
  StepTrace,
  operationId,
  renameError,
} from '../../rename-api';
import { RenamerFile } from '../../renamer-file';
import { PluginEditorComponent } from '../plugin-editor/plugin-editor.component';
import { TmdbPickerComponent } from '../tmdb-picker/tmdb-picker.component';

@Component({
  imports: [PluginEditorComponent, TmdbPickerComponent, RouterLink],
  selector: 'app-rename-workflow',
  styleUrl: './rename-workflow.component.scss',
  templateUrl: './rename-workflow.component.html',
})
export class RenameWorkflowComponent {
  readonly files = input.required<RenamerFile[]>();
  private readonly api = inject(RenameApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly catalog = signal<PluginDescriptor[]>([]);
  protected readonly catalogLoaded = signal(false);
  protected readonly plugins = signal<PluginInstance[]>([]);
  protected readonly selections = signal<MetadataSelection[]>([]);
  protected readonly preview = signal<PreviewResult | null>(null);
  protected readonly plan = signal<PlanView | null>(null);
  protected readonly matching = signal<MatchContext | null>(null);
  protected readonly loadingPreview = signal(false);
  protected readonly preparing = signal(false);
  protected readonly executing = signal(false);
  protected readonly error = signal('');
  protected readonly catalogError = signal('');
  protected readonly planId = signal('');
  private revision = 0;
  private previewRevision = -1;
  private planRevision = -1;
  private matchingRevision = -1;
  private previewStarted = false;
  private request?: Subscription;
  private preparation?: Subscription;
  private polling?: Subscription;
  private debounce?: ReturnType<typeof setTimeout>;
  private executeOperation = '';

  constructor() {
    this.loadCatalog();
    effect(() => {
      this.files();
      untracked(() => this.invalidate(true));
    });
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.debounce);
      this.request?.unsubscribe();
      this.preparation?.unsubscribe();
      this.polling?.unsubscribe();
    });
  }

  protected loadCatalog(): void {
    this.catalogError.set('');
    this.api
      .catalog()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this.catalog.set(catalog);
          this.catalogLoaded.set(true);
        },
        error: (error: unknown) => this.catalogError.set(renameError(error)),
      });
  }

  protected changePlugins(plugins: PluginInstance[]): void {
    this.plugins.set(plugins);
    this.invalidate(true);
  }

  protected refreshPreview(): void {
    clearTimeout(this.debounce);
    this.request?.unsubscribe();
    if (!this.files().length || this.files().length > 200) return;
    this.previewStarted = true;
    const revision = this.revision;
    this.loadingPreview.set(true);
    this.error.set('');
    this.request = this.api
      .preview(this.snapshot())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => {
          if (revision !== this.revision) return;
          this.preview.set(preview);
          this.previewRevision = revision;
          this.loadingPreview.set(false);
        },
        error: (error: unknown) => {
          if (revision !== this.revision) return;
          this.loadingPreview.set(false);
          this.error.set(renameError(error));
        },
      });
  }

  protected canPrepare(): boolean {
    return (
      !!this.preview()?.canPrepare &&
      this.previewRevision === this.revision &&
      !this.loadingPreview() &&
      !this.preparing() &&
      !this.executing() &&
      !!this.preview()?.files.some((file) => !file.unchanged)
    );
  }

  protected prepare(): void {
    if (!this.canPrepare()) return;
    const revision = this.revision;
    this.preparing.set(true);
    this.plan.set(null);
    this.error.set('');
    this.preparation = this.api
      .prepare(this.snapshot())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (accepted) => {
          if (revision !== this.revision) return;
          this.planId.set(accepted.id);
          this.pollPlan(accepted.id, revision);
        },
        error: (error: unknown) => {
          if (revision !== this.revision) return;
          this.preparing.set(false);
          this.error.set(renameError(error));
        },
      });
  }

  protected retryPlan(): void {
    if (this.planId()) this.pollPlan(this.planId(), this.revision);
  }

  protected canConfirm(): boolean {
    return (
      this.plan()?.status === 'Ready' &&
      this.planRevision === this.revision &&
      !this.plan()?.issues.length &&
      !this.preparing() &&
      !this.loadingPreview() &&
      !this.executing()
    );
  }

  protected confirm(): void {
    const plan = this.plan();
    if (!plan || !this.canConfirm()) return;
    const revision = this.revision;
    this.executing.set(true);
    this.error.set('');
    this.executeOperation ||= operationId();
    this.api
      .execute(plan.id, plan.version, this.executeOperation)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (accepted) => {
          // Accepted jobs are durable even if the user has changed the editor.
          void this.router.navigate(['/runs', accepted.id]);
        },
        error: (error: unknown) => {
          this.executing.set(false);
          if (revision === this.revision) this.error.set(renameError(error));
        },
      });
  }

  protected changedCount(): number {
    return this.plan()?.files.filter((file) => file.sourcePath !== file.targetPath).length ?? 0;
  }

  protected folder(path: string): string {
    const separator = path.lastIndexOf('/');
    return separator < 0 ? 'Server root' : path.slice(0, separator);
  }

  protected match(fileId: string, step: StepTrace): void {
    const plugin = this.plugins().find((entry) => entry.instanceId === step.instanceId);
    if (!plugin || this.previewRevision !== this.revision) return;
    this.matchingRevision = this.revision;
    this.matching.set({ fileId, plugin, inputPath: step.inputPath });
  }

  protected acceptMatch(selection: MetadataSelection): void {
    if (this.matchingRevision !== this.revision) return;
    const index = this.plugins().findIndex((plugin) => plugin.instanceId === selection.instanceId);
    const downstream = new Set(
      this.plugins()
        .slice(index)
        .map((plugin) => plugin.instanceId),
    );
    this.selections.update((selections) => [
      ...selections.filter(
        (entry) => entry.fileId !== selection.fileId || !downstream.has(entry.instanceId),
      ),
      selection,
    ]);
    this.matching.set(null);
    this.invalidate(false);
  }

  private snapshot(): PipelineRequest {
    return {
      files: this.files().map((file) => ({ id: file.path, path: file.path })),
      plugins: this.plugins(),
      selections: this.selections(),
    };
  }

  private invalidate(clearSelections: boolean): void {
    this.revision++;
    this.previewRevision = -1;
    this.planRevision = -1;
    this.request?.unsubscribe();
    this.preparation?.unsubscribe();
    this.polling?.unsubscribe();
    clearTimeout(this.debounce);
    this.preview.set(null);
    this.plan.set(null);
    this.planId.set('');
    this.matching.set(null);
    this.loadingPreview.set(false);
    this.preparing.set(false);
    this.error.set('');
    this.executeOperation = '';
    if (clearSelections) this.selections.set([]);
    if (this.previewStarted && !this.executing()) {
      this.debounce = setTimeout(() => this.refreshPreview(), 300);
    }
  }

  private pollPlan(id: string, revision: number): void {
    this.polling?.unsubscribe();
    this.preparing.set(true);
    this.error.set('');
    this.polling = timer(0, 750)
      .pipe(
        exhaustMap(() => this.api.plan(id)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (plan) => {
          if (revision !== this.revision) return;
          this.plan.set(plan);
          if (plan.status !== 'Preparing') {
            this.preparing.set(false);
            this.planRevision = revision;
            this.polling?.unsubscribe();
          }
        },
        error: (error: unknown) => {
          if (revision !== this.revision) return;
          this.preparing.set(false);
          this.error.set(renameError(error));
        },
      });
  }
}
