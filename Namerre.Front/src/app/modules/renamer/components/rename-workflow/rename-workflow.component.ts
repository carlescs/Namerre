import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  type ElementRef,
  effect,
  Injector,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import {
  catchError,
  concatMap,
  defer,
  finalize,
  from,
  of,
  retry,
  type Subscription,
  tap,
  throwError,
  timer,
} from 'rxjs';
import { WorkflowMode, type WorkflowModeValue } from '../../../../workflow-mode/workflow-mode';
import { WorkloadLimits } from '../../../../workload-limits';
import { detectCompanions } from '../../companions';
import { loadPipelineDraft, saveFilesDraft, savePipelineDraft } from '../../draft-storage';
import { tmdbOptionsHash } from '../../options-hash';
import {
  awaitingMetadata,
  HIGH_CONFIDENCE,
  isMatchingIssue,
  type MatchContext,
  type MatchSuggestion,
  type MetadataSelection,
  operationId,
  type PipelineRequest,
  type PlanView,
  type PluginDescriptor,
  type PluginInstance,
  type PresetView,
  type PreviewResult,
  RenameApi,
  renameError,
  type StepTrace,
  type TargetOverride,
  type TmdbMatchRow,
  type TmdbPickerState,
} from '../../rename-api';
import { RenameEvents, refreshOn } from '../../rename-events';
import type { RenamerFile } from '../../renamer-file';
import { matchSimplePreset, simpleCompatible, simpleRunOptions } from '../../simple-presets';
import { tmdbFilename } from '../../tmdb-filename';
import { PluginConfigurationComponent } from '../plugin-configuration/plugin-configuration.component';
import { PresetPickerComponent } from '../preset-picker/preset-picker.component';
import { RenameConfirmationComponent } from '../rename-confirmation/rename-confirmation.component';
import { RenamePreviewComponent } from '../rename-preview/rename-preview.component';
import { TmdbMatchingComponent } from '../tmdb-matching/tmdb-matching.component';
import { TmdbPickerComponent } from '../tmdb-picker/tmdb-picker.component';

/**
 * Advanced mode: files → plugins → [matching] → preview → confirmation.
 * Simple mode: files → preset → [matching] → review (the preview, then the prepared plan).
 */
type WizardStage =
  | 'files'
  | 'plugins'
  | 'preset'
  | 'matching'
  | 'preview'
  | 'confirmation'
  | 'review';

/** The screen shown for a stage; Simple's review shows the preview or the confirmation. */
type WizardScreen = Exclude<WizardStage, 'review'>;

/** Retries of a suggestion TMDB refused with 429 (busy or rate-limited), after 1, 2 and 4 seconds. */
const SUGGESTION_RETRIES = 3;

@Component({
  imports: [
    PluginConfigurationComponent,
    PresetPickerComponent,
    RenameConfirmationComponent,
    RenamePreviewComponent,
    TmdbMatchingComponent,
    TmdbPickerComponent,
  ],
  selector: 'app-rename-workflow',
  styleUrl: './rename-workflow.component.scss',
  templateUrl: './rename-workflow.component.html',
})
export class RenameWorkflowComponent {
  readonly files = input.required<RenamerFile[]>();
  /** Related files (subtitles, .nfo, artwork) follow their video instead of running the steps. */
  readonly includeRelated = input(true);
  readonly fileRemoved = output<string>();
  private readonly api = inject(RenameApi);
  private readonly events = inject(RenameEvents);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly limits = inject(WorkloadLimits);
  private readonly workflowMode = inject(WorkflowMode);
  protected readonly mode = this.workflowMode.mode;
  protected readonly maxFiles = computed(() => this.limits.current().maxFilesPerRun);
  private readonly stageHeading =
    viewChild.required<ElementRef<HTMLHeadingElement>>('stageHeading');
  protected readonly stage = signal<WizardStage>('files');
  protected readonly backwards = signal(false);
  protected readonly stages = computed(() => [
    {
      id: 'files',
      title: 'Select files',
      description:
        'Build your collection from server folders. Add files from one folder or bring several together.',
    },
    this.mode() === 'simple'
      ? {
          id: 'preset',
          title: 'Choose a preset',
          description:
            'Pick what kind of renaming you need. Switch to Advanced mode for full control over every step.',
        }
      : {
          id: 'plugins',
          title: 'Configure plugins',
          description:
            'Build a sequence of renaming rules. Each enabled plugin works on the output of the one before it.',
        },
    ...(this.tmdbPlugins().length
      ? [
          {
            id: 'matching',
            title: 'Match TMDB metadata',
            description: 'Review and confirm the movie or episode metadata used to name each file.',
          },
        ]
      : []),
    ...(this.mode() === 'simple'
      ? [
          {
            id: 'review',
            title: 'Review and rename',
            description:
              'Check the proposed names, then confirm. Nothing is renamed until you click the rename button.',
          },
        ]
      : [
          {
            id: 'preview',
            title: 'Review filenames',
            description:
              'Compare the original and proposed paths. Resolve any conflicts before preparing your plan.',
          },
          {
            id: 'confirmation',
            title: 'Confirm renaming',
            description:
              'Review the exact prepared changes. Only your explicit confirmation can rename files.',
          },
        ]),
  ]);
  /** Whether Simple's review shows the prepared plan instead of the preview. */
  protected readonly showingPlan = computed(
    () => this.preparing() || !!this.plan() || !!this.planId(),
  );
  protected readonly screen = computed<WizardScreen>(() => {
    const stage = this.stage();
    if (stage !== 'review') return stage;
    return this.showingPlan() ? 'confirmation' : 'preview';
  });
  protected readonly stageIndex = computed(() =>
    this.stages().findIndex((stage) => stage.id === this.stage()),
  );
  protected readonly validCollection = computed(
    () => this.files().length > 0 && this.files().length <= this.maxFiles(),
  );
  protected readonly catalog = signal<PluginDescriptor[]>([]);
  protected readonly catalogLoaded = signal(false);
  private catalogLoading = false;
  private readonly draft = loadPipelineDraft();
  protected readonly plugins = signal<PluginInstance[]>(this.draft?.plugins ?? []);
  protected readonly selections = signal<MetadataSelection[]>([]);
  /** Hand-edited target paths; dropped whenever the plugins or matches change the computed names. */
  protected readonly overrides = signal<TargetOverride[]>([]);
  protected readonly checkingOverride = signal(false);
  protected readonly verifyContent = signal(this.draft?.verifyContent ?? false);
  protected readonly deleteEmptyFolders = signal(this.draft?.deleteEmptyFolders ?? true);
  protected readonly numberCollisions = signal(this.draft?.numberCollisions ?? false);
  protected readonly preview = signal<PreviewResult | null>(null);
  protected readonly plan = signal<PlanView | null>(null);
  protected readonly matching = signal<MatchContext | null>(null);
  protected readonly loadingPreview = signal(false);
  protected readonly preparing = signal(false);
  protected readonly executing = signal(false);
  protected readonly error = signal('');
  protected readonly catalogError = signal('');
  protected readonly planId = signal('');
  protected readonly suggesting = signal(false);
  protected readonly searchType = signal('movie');
  protected readonly suggestProgress = signal({ done: 0, total: 0 });
  private readonly suggestions = signal<MatchSuggestion[]>([]);
  /** The Simple preset that builds the current steps, if any. */
  protected readonly simplePreset = computed(
    () => matchSimplePreset(this.plugins())?.preset ?? null,
  );
  protected readonly tmdbPlugins = computed(() =>
    this.plugins().filter((plugin) => plugin.enabled && plugin.typeId === 'tmdb'),
  );
  /** Related file paths and the video each follows, as the server will pair them. */
  protected readonly companions = computed(() =>
    this.includeRelated()
      ? detectCompanions(this.files().map((file) => file.path))
      : new Map<string, string>(),
  );
  protected readonly matchRows = computed(() =>
    this.files()
      .filter(
        (file) =>
          !this.companions().has(file.path) &&
          !this.preview()?.files.find((entry) => entry.id === file.path)?.companionOf,
      )
      .flatMap((file) =>
        this.tmdbPlugins().map((plugin) => ({
          fileId: file.path,
          plugin,
          position: this.plugins().indexOf(plugin) + 1,
          suggestion: this.suggestions().find(
            (entry) => entry.fileId === file.path && entry.instanceId === plugin.instanceId,
          ),
          step: this.preview()
            ?.files.find((entry) => entry.id === file.path)
            ?.steps.find((step) => step.instanceId === plugin.instanceId),
        })),
      )
      // A step whose "Apply to" filter skips the file needs no match.
      .filter((row) => !row.step?.skipped),
  );
  protected readonly metadataPending = computed(() => this.preview()?.files.some(awaitingMetadata));
  protected readonly unmatchedFiles = computed(
    () => this.preview()?.files.filter(awaitingMetadata).length ?? 0,
  );
  /** Verified high-confidence proposals; lower ones are reviewed and accepted one by one. */
  protected readonly pendingMatches = computed(() =>
    this.matchRows().flatMap((row): MetadataSelection[] => {
      const suggestion = row.suggestion;
      return row.step?.error &&
        isMatchingIssue(row.step.error.code) &&
        suggestion?.match?.confirmationToken &&
        (suggestion.confidence ?? 0) >= HIGH_CONFIDENCE &&
        suggestion.inputPath === row.step.inputPath &&
        suggestion.optionsHash === tmdbOptionsHash(row.plugin.options)
        ? [
            {
              fileId: suggestion.fileId,
              instanceId: suggestion.instanceId,
              inputPath: suggestion.inputPath,
              optionsHash: suggestion.optionsHash,
              confirmationToken: suggestion.match.confirmationToken,
            },
          ]
        : [];
    }),
  );
  private readonly pickerStates = new Map<string, Map<string, TmdbPickerState>>();
  private readonly showStates = new Map<string, TmdbPickerState>();
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
  private suggestionRequest?: Subscription;
  private pendingSuggestions = false;

  constructor() {
    effect(() => {
      this.files();
      this.includeRelated();
      untracked(() => this.invalidate());
    });
    effect(() => {
      const mode = this.mode();
      untracked(() => this.modeChanged(mode));
    });
    let discarded = this.workflowMode.discarded();
    effect(() => {
      const count = this.workflowMode.discarded();
      if (count === discarded) return;
      discarded = count;
      untracked(() => this.discardSteps());
    });
    effect(() =>
      savePipelineDraft({
        plugins: this.plugins(),
        verifyContent: this.verifyContent(),
        deleteEmptyFolders: this.deleteEmptyFolders(),
        numberCollisions: this.numberCollisions(),
      }),
    );
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.debounce);
      this.request?.unsubscribe();
      this.preparation?.unsubscribe();
      this.polling?.unsubscribe();
      this.suggestionRequest?.unsubscribe();
    });
  }

  protected next(): void {
    if (!this.validCollection() || this.preparing() || this.executing()) return;
    switch (this.stage()) {
      case 'files':
        this.goTo(this.mode() === 'simple' ? 'preset' : 'plugins');
        if (!this.catalogLoaded() && !this.catalogLoading) this.loadCatalog();
        break;
      case 'plugins':
        if (!this.catalogLoaded()) return;
        this.goTo(this.tmdbPlugins().length ? 'matching' : 'preview');
        if (this.previewRevision !== this.revision) this.refreshPreview();
        break;
      case 'preset':
        if (!this.simplePreset()) return;
        this.goTo(this.tmdbPlugins().length ? 'matching' : 'review');
        if (this.previewRevision !== this.revision) this.refreshPreview();
        break;
      case 'matching':
        if (this.canReview()) this.goTo(this.reviewStage());
        break;
    }
  }

  protected back(): void {
    if (this.preparing() || this.executing()) return;
    this.suggestionRequest?.unsubscribe();
    this.suggesting.set(false);
    this.pendingSuggestions = false;
    this.matching.set(null);
    switch (this.stage()) {
      case 'plugins':
      case 'preset':
        this.goTo('files');
        break;
      case 'matching':
        this.goTo(this.editStage());
        break;
      case 'preview':
      case 'review':
        this.goTo(this.tmdbPlugins().length ? 'matching' : this.editStage());
        break;
      case 'confirmation':
        this.goTo('preview');
        break;
    }
  }

  protected canReview(): boolean {
    return (
      this.validCollection() &&
      this.previewRevision === this.revision &&
      !this.loadingPreview() &&
      !this.suggesting() &&
      !this.metadataPending()
    );
  }

  /** Where steps are edited in the current mode. */
  private editStage(): WizardStage {
    return this.mode() === 'simple' ? 'preset' : 'plugins';
  }

  /** Where names are reviewed in the current mode. */
  private reviewStage(): WizardStage {
    return this.mode() === 'simple' ? 'review' : 'preview';
  }

  /**
   * Moves the open stage to its counterpart in the new mode; steps, files, matches and any plan
   * carry over. Simple runs use its fixed run options, so they are applied when its steps are kept.
   */
  private modeChanged(mode: WorkflowModeValue): void {
    if (mode === 'simple' && simpleCompatible(this.plugins())) this.applySimpleRunOptions();
    const stage = this.stage();
    const counterparts: Partial<Record<WizardStage, WizardStage>> =
      mode === 'simple'
        ? { plugins: 'preset', preview: 'review', confirmation: 'review' }
        : {
            preset: 'plugins',
            review: this.showingPlan() ? 'confirmation' : 'preview',
          };
    const target = counterparts[stage];
    if (target) {
      this.stage.set(target);
      afterNextRender(() => this.stageHeading().nativeElement.focus(), { injector: this.injector });
    }
    if (mode === 'advanced' && stage !== 'files' && !this.catalogLoaded() && !this.catalogLoading)
      this.loadCatalog();
  }

  /** The user discarded the steps to switch to Simple: start the pipeline afresh. */
  private discardSteps(): void {
    this.plugins.set([]);
    this.applySimpleRunOptions();
    this.overrides.set([]);
    this.invalidate();
  }

  private applySimpleRunOptions(): void {
    if (this.numberCollisions() !== simpleRunOptions.numberCollisions) {
      this.numberCollisions.set(simpleRunOptions.numberCollisions);
      this.overrides.set([]);
      this.invalidate();
    }
    this.verifyContent.set(simpleRunOptions.verifyContent);
    this.deleteEmptyFolders.set(simpleRunOptions.deleteEmptyFolders);
  }

  /** A Simple preset or one of its options built new steps. */
  protected choosePreset(plugins: PluginInstance[]): void {
    this.changePlugins(plugins);
    this.applySimpleRunOptions();
    const mediaType = matchSimplePreset(plugins)?.preset.tmdbMediaType;
    if (mediaType) this.searchType.set(mediaType);
  }

  private goTo(stage: WizardStage): void {
    this.backwards.set(this.stages().findIndex((item) => item.id === stage) < this.stageIndex());
    this.stage.set(stage);
    afterNextRender(() => this.stageHeading().nativeElement.focus(), { injector: this.injector });
  }

  protected loadCatalog(): void {
    this.catalogLoading = true;
    this.catalogError.set('');
    this.api
      .catalog()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this.catalog.set(catalog);
          this.catalogLoaded.set(true);
          this.catalogLoading = false;
        },
        error: (error: unknown) => {
          this.catalogLoading = false;
          this.catalogError.set(renameError(error));
        },
      });
  }

  protected changePlugins(plugins: PluginInstance[]): void {
    for (const plugin of plugins) {
      const previous = this.plugins().find((entry) => entry.instanceId === plugin.instanceId);
      if (previous && previous.options.language !== plugin.options.language) {
        this.pickerStates.delete(plugin.instanceId);
        this.showStates.delete(plugin.instanceId);
      }
    }
    this.plugins.set(plugins);
    this.overrides.set([]);
    this.invalidate();
  }

  /** A loaded preset already replaced the steps through the editor; take over its run options. */
  protected applyPreset(preset: PresetView): void {
    this.verifyContent.set(preset.verifyContent);
    this.deleteEmptyFolders.set(preset.deleteEmptyFolders);
    this.setNumberCollisions(preset.onCollision === 'AddNumber');
  }

  /** Collision handling changes the computed names, so the preview is taken again. */
  protected setNumberCollisions(on: boolean): void {
    if (on === this.numberCollisions()) return;
    this.numberCollisions.set(on);
    this.overrides.set([]);
    this.invalidate();
  }

  protected refreshPreview(): void {
    clearTimeout(this.debounce);
    this.request?.unsubscribe();
    if (!this.files().length || this.files().length > this.maxFiles()) return;
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
          this.selections.update((selections) =>
            selections.filter((selection) => {
              const step = preview.files
                .find((file) => file.id === selection.fileId)
                ?.steps.find((step) => step.instanceId === selection.instanceId);
              return (
                !step ||
                (step.inputPath === selection.inputPath && !isMatchingIssue(step.error?.code ?? ''))
              );
            }),
          );
          this.preview.set(preview);
          this.previewRevision = revision;
          this.loadingPreview.set(false);
          this.suggestions.update((suggestions) =>
            suggestions.filter((suggestion) =>
              preview.files
                .find((file) => file.id === suggestion.fileId)
                ?.steps.some(
                  (step) =>
                    step.instanceId === suggestion.instanceId &&
                    step.inputPath === suggestion.inputPath,
                ),
            ),
          );
          if (this.pendingSuggestions) {
            this.pendingSuggestions = false;
            this.suggestMatches(this.searchType());
          }
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
    this.goTo(this.mode() === 'simple' ? 'review' : 'confirmation');
    const revision = this.revision;
    this.preparing.set(true);
    this.plan.set(null);
    this.planId.set('');
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
      !this.checkingOverride() &&
      !this.executing()
    );
  }

  /**
   * Replaces the computed new name of one file (or restores it when `targetPath` is null). The
   * edited pipeline is previewed first, so a rejected name leaves the prepared plan untouched;
   * an accepted one prepares the plan again.
   */
  protected overrideTarget(fileId: string, targetPath: string | null): void {
    if (!this.canConfirm()) return;
    const overrides = [
      ...this.overrides().filter((entry) => entry.fileId !== fileId),
      ...(targetPath === null ? [] : [{ fileId, targetPath }]),
    ];
    const revision = this.revision;
    this.checkingOverride.set(true);
    this.error.set('');
    this.request?.unsubscribe();
    this.request = this.api
      .preview({ ...this.snapshot(), overrides })
      .pipe(
        finalize(() => this.checkingOverride.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (preview) => {
          if (revision !== this.revision) return;
          const issue = preview.files.find((file) => file.id === fileId)?.issues[0];
          if (issue) {
            this.error.set(`The new name cannot be used: ${issue.message}`);
            return;
          }
          this.overrides.set(overrides);
          this.invalidate(true);
          this.preview.set(preview);
          this.previewRevision = this.revision;
          if (this.canPrepare()) this.prepare();
          else this.goTo(this.reviewStage());
        },
        error: (error: unknown) => {
          if (revision === this.revision) this.error.set(renameError(error));
        },
      });
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
          // Accepted jobs are durable even if the user has changed the editor. The renamed
          // paths no longer exist, so the saved collection is dropped; the rules are kept.
          saveFilesDraft([]);
          void this.router.navigate(['/runs', accepted.id]);
        },
        error: (error: unknown) => {
          this.executing.set(false);
          if (revision === this.revision) this.error.set(renameError(error));
        },
      });
  }

  protected match(fileId: string, step: StepTrace): void {
    const plugin = this.plugins().find((entry) => entry.instanceId === step.instanceId);
    if (!plugin || this.previewRevision !== this.revision) return;
    this.matchingRevision = this.revision;
    const saved = this.pickerStates.get(plugin.instanceId)?.get(fileId);
    const show = this.showStates.get(plugin.instanceId);
    const inferred = tmdbFilename(step.inputPath, this.searchType());
    this.matching.set({
      fileId,
      plugin,
      inputPath: step.inputPath,
      state:
        saved ??
        (show
          ? {
              ...show,
              season: inferred.season ?? show.season,
              episode: inferred.episode ?? 1,
              episodeEnd: inferred.episodeEnd ?? null,
            }
          : {
              query: inferred.query,
              mediaType: this.searchType(),
              language: plugin.options.language ?? 'en-US',
              season: inferred.season ?? 1,
              episode: inferred.episode ?? 1,
              episodeEnd: inferred.episodeEnd ?? null,
              matches: [],
              selected: null,
            }),
      reusedShow: !saved && !!show,
    });
  }

  protected rememberMatch(state: TmdbPickerState): void {
    const context = this.matching();
    if (!context || this.matchingRevision !== this.revision) return;
    this.pickerState(context.plugin.instanceId).set(context.fileId, state);
    if (state.selected?.mediaType === 'tv') {
      this.showStates.set(context.plugin.instanceId, {
        ...state,
        query: state.selected.title,
        matches: [state.selected],
      });
    }
  }

  protected acceptMatch(selection: MetadataSelection): void {
    if (this.matchingRevision !== this.revision) return;
    const match = this.pickerStates.get(selection.instanceId)?.get(selection.fileId)?.selected;
    if (match?.confirmationToken === selection.confirmationToken) {
      this.setSuggestion({ ...selection, match, error: '' });
    }
    this.selections.update((selections) => [
      ...selections.filter(
        (entry) => entry.fileId !== selection.fileId || entry.instanceId !== selection.instanceId,
      ),
      selection,
    ]);
    this.matching.set(null);
    this.overrides.set([]);
    this.invalidate();
  }

  protected suggestMatches(mediaType: string): void {
    if (
      this.suggesting() ||
      this.preparing() ||
      this.executing() ||
      !this.files().length ||
      this.files().length > this.maxFiles()
    )
      return;
    this.searchType.set(mediaType);
    if (this.previewRevision !== this.revision) {
      this.pendingSuggestions = true;
      this.refreshPreview();
      return;
    }
    const rows = this.matchRows().filter(
      (row): row is TmdbMatchRow & { step: StepTrace } =>
        !!row.step &&
        (!row.step.error || isMatchingIssue(row.step.error.code)) &&
        !this.selections().some(
          (entry) => entry.fileId === row.fileId && entry.instanceId === row.plugin.instanceId,
        ),
    );
    this.suggesting.set(true);
    this.suggestProgress.set({ done: 0, total: rows.length });
    // One request per file: the server parses the name, searches, scores and verifies, and its
    // TMDB cache makes repeated searches for the episodes of one show cheap.
    this.suggestionRequest = from(rows)
      .pipe(
        concatMap((row) =>
          this.suggestFor(row, mediaType).pipe(
            finalize(() => this.suggestProgress.update((p) => ({ ...p, done: p.done + 1 }))),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ complete: () => this.suggesting.set(false) });
  }

  private suggestFor(row: TmdbMatchRow & { step: StepTrace }, mediaType: string) {
    return defer(() => {
      const binding = {
        fileId: row.fileId,
        instanceId: row.plugin.instanceId,
        inputPath: row.step.inputPath,
        optionsHash: tmdbOptionsHash(row.plugin.options),
      };
      const language = row.plugin.options.language ?? 'en-US';
      this.setSuggestion({ ...binding, match: null, error: '' });
      return this.api.suggestTmdb({ ...binding, mediaType, language }).pipe(
        // TMDB allows four lookups at a time and may rate-limit; wait and retry a few times.
        retry({
          count: SUGGESTION_RETRIES,
          delay: (error: unknown, attempt) =>
            error instanceof HttpErrorResponse && error.status === 429
              ? timer(1000 * 2 ** (attempt - 1))
              : throwError(() => error),
        }),
        tap((suggestion) => {
          const parsed = suggestion.parsed;
          this.pickerState(row.plugin.instanceId).set(row.fileId, {
            query: parsed.query,
            mediaType,
            language,
            season: parsed.season ?? 1,
            episode: parsed.episode ?? 1,
            episodeEnd: parsed.episodeEnd,
            matches: suggestion.candidates,
            selected: suggestion.match,
          });
          this.setSuggestion({
            ...binding,
            match: suggestion.match,
            confidence: suggestion.confidence,
            error: suggestion.problem ?? '',
          });
        }),
        catchError((error: unknown) => {
          this.setSuggestion({ ...binding, match: null, error: renameError(error) });
          return of(null);
        }),
      );
    });
  }

  private pickerState(instanceId: string): Map<string, TmdbPickerState> {
    let states = this.pickerStates.get(instanceId);
    if (!states) {
      states = new Map();
      this.pickerStates.set(instanceId, states);
    }
    return states;
  }

  protected acceptSuggestion(suggestion: MatchSuggestion): void {
    if (
      !suggestion.match?.confirmationToken ||
      this.previewRevision !== this.revision ||
      this.suggesting()
    )
      return;
    this.matchingRevision = this.revision;
    this.acceptMatch({
      fileId: suggestion.fileId,
      instanceId: suggestion.instanceId,
      inputPath: suggestion.inputPath,
      optionsHash: suggestion.optionsHash,
      confirmationToken: suggestion.match.confirmationToken,
    });
  }

  protected acceptAllMatches(): void {
    if (
      this.previewRevision !== this.revision ||
      this.loadingPreview() ||
      this.suggesting() ||
      this.preparing() ||
      this.executing()
    )
      return;
    const accepted = this.pendingMatches();
    if (!accepted.length) return;
    this.selections.update((selections) => [
      ...selections.filter(
        (selection) =>
          !accepted.some(
            (entry) =>
              entry.fileId === selection.fileId && entry.instanceId === selection.instanceId,
          ),
      ),
      ...accepted,
    ]);
    this.overrides.set([]);
    this.invalidate();
  }

  private setSuggestion(suggestion: MatchSuggestion): void {
    this.suggestions.update((suggestions) => [
      ...suggestions.filter(
        (entry) => entry.fileId !== suggestion.fileId || entry.instanceId !== suggestion.instanceId,
      ),
      suggestion,
    ]);
  }

  private snapshot(): PipelineRequest {
    return {
      files: this.files().map((file) => ({ id: file.path, path: file.path })),
      plugins: this.plugins(),
      selections: this.selections(),
      verifyContent: this.verifyContent(),
      deleteEmptyFolders: this.deleteEmptyFolders(),
      overrides: this.overrides(),
      includeCompanions: this.includeRelated(),
      onCollision: this.numberCollisions() ? 'AddNumber' : 'Report',
    };
  }

  /** `overriding` keeps the confirmation stage open: the caller supplies the fresh preview. */
  private invalidate(overriding = false): void {
    this.revision++;
    this.previewRevision = -1;
    this.planRevision = -1;
    this.request?.unsubscribe();
    this.preparation?.unsubscribe();
    this.polling?.unsubscribe();
    this.suggestionRequest?.unsubscribe();
    this.suggesting.set(false);
    this.pendingSuggestions = false;
    clearTimeout(this.debounce);
    this.preview.set(null);
    this.plan.set(null);
    this.planId.set('');
    this.matching.set(null);
    this.loadingPreview.set(false);
    this.preparing.set(false);
    this.error.set('');
    this.executeOperation = '';
    const files = new Set(this.files().map((file) => file.path));
    this.overrides.update((overrides) => overrides.filter((entry) => files.has(entry.fileId)));
    const plugins = new Map(
      this.plugins()
        .filter((plugin) => plugin.typeId === 'tmdb')
        .map((plugin) => [plugin.instanceId, tmdbOptionsHash(plugin.options)]),
    );
    this.selections.update((selections) =>
      selections.filter(
        (selection) =>
          files.has(selection.fileId) &&
          plugins.get(selection.instanceId) === selection.optionsHash,
      ),
    );
    this.suggestions.update((suggestions) =>
      suggestions.filter(
        (suggestion) =>
          files.has(suggestion.fileId) &&
          plugins.get(suggestion.instanceId) === suggestion.optionsHash,
      ),
    );
    for (const [instanceId, states] of this.pickerStates) {
      if (!plugins.has(instanceId)) {
        this.pickerStates.delete(instanceId);
        this.showStates.delete(instanceId);
      } else {
        for (const fileId of states.keys()) {
          if (!files.has(fileId)) states.delete(fileId);
        }
      }
    }
    if (overriding) return;
    if (this.stage() === 'confirmation') this.goTo('preview');
    if (this.stage() === 'matching' && !this.tmdbPlugins().length) this.goTo(this.editStage());
    if (
      this.stage() !== 'files' &&
      (this.previewStarted || this.tmdbPlugins().length > 0) &&
      !this.executing()
    ) {
      this.debounce = setTimeout(() => this.refreshPreview(), 300);
    }
  }

  private pollPlan(id: string, revision: number): void {
    this.polling?.unsubscribe();
    this.preparing.set(true);
    this.error.set('');
    this.polling = refreshOn(this.events.watch('Plan', id), () => this.api.plan(id))
      .pipe(takeUntilDestroyed(this.destroyRef))
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
