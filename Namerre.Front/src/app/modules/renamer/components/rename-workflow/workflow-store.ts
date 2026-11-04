import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, effect, Injectable, inject, signal, untracked } from '@angular/core';
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
  Subject,
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
import { operationId, RenameApi, renameError } from '../../rename-api';
import { RenameEvents, refreshOn } from '../../rename-events';
import {
  awaitingMetadata,
  HIGH_CONFIDENCE,
  isMatchingIssue,
  type MatchContext,
  type MatchSuggestion,
  type MetadataSelection,
  type PipelineRequest,
  type PlanView,
  type PluginDescriptor,
  type PluginInstance,
  type PresetView,
  type PreviewResult,
  type StepTrace,
  type TargetOverride,
  type TmdbMatchRow,
  type TmdbPickerState,
} from '../../rename-models';
import type { RenamerFile } from '../../renamer-file';
import { matchSimplePreset, simpleCompatible, simpleRunOptions } from '../../simple-presets';
import { tmdbFilename } from '../../tmdb-filename';

/**
 * Advanced mode: files → plugins → [matching] → preview → confirmation.
 * Simple mode: files → preset → [matching] → review (the preview, then the prepared plan).
 */
export type WizardStage =
  | 'files'
  | 'plugins'
  | 'preset'
  | 'matching'
  | 'preview'
  | 'confirmation'
  | 'review';

/** The screen shown for a stage; Simple's review shows the preview or the confirmation. */
export type WizardScreen = Exclude<WizardStage, 'review'>;

export interface WizardStep {
  id: WizardStage;
  title: string;
  description: string;
}

/** Retries of a suggestion TMDB refused with 429 (busy or rate-limited), after 1, 2 and 4 seconds. */
const SUGGESTION_RETRIES = 3;

/**
 * State and transitions of the rename wizard: the collection, the step draft, the stage, and the
 * preview → prepare → execute lifecycle with TMDB matching. Every change to what the names depend
 * on bumps a revision, so late responses for an older pipeline are ignored.
 *
 * Provided by `RenameWorkflowComponent`, which renders it and feeds it the collection through
 * `setCollection`; it lives and is cleaned up with that component.
 */
@Injectable()
export class WorkflowStore {
  private readonly api = inject(RenameApi);
  private readonly events = inject(RenameEvents);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly limits = inject(WorkloadLimits);
  private readonly workflowMode = inject(WorkflowMode);

  /** Emits whenever the wizard moves to another stage, so the view can move focus. */
  private readonly entered = new Subject<void>();
  readonly stageEntered = this.entered.asObservable();

  readonly mode = this.workflowMode.mode;
  readonly maxFiles = computed(() => this.limits.current().maxFilesPerRun);

  private readonly _files = signal<RenamerFile[]>([]);
  readonly files = this._files.asReadonly();
  /** Related files (subtitles, .nfo, artwork) follow their video instead of running the steps. */
  private readonly _includeRelated = signal(true);
  readonly includeRelated = this._includeRelated.asReadonly();

  private readonly _stage = signal<WizardStage>('files');
  readonly stage = this._stage.asReadonly();
  /** Whether the last move went to an earlier stage (drives the slide direction). */
  private readonly _backwards = signal(false);
  readonly backwards = this._backwards.asReadonly();

  private readonly _catalog = signal<PluginDescriptor[]>([]);
  readonly catalog = this._catalog.asReadonly();
  private readonly _catalogLoaded = signal(false);
  readonly catalogLoaded = this._catalogLoaded.asReadonly();
  private readonly _catalogError = signal('');
  readonly catalogError = this._catalogError.asReadonly();
  private catalogLoading = false;

  private readonly draft = loadPipelineDraft();
  private readonly _plugins = signal<PluginInstance[]>(this.draft?.plugins ?? []);
  readonly plugins = this._plugins.asReadonly();
  private readonly _selections = signal<MetadataSelection[]>([]);
  readonly selections = this._selections.asReadonly();
  /** Hand-edited target paths; dropped whenever the plugins or matches change the computed names. */
  private readonly _overrides = signal<TargetOverride[]>([]);
  readonly overrides = this._overrides.asReadonly();
  private readonly _checkingOverride = signal(false);
  readonly checkingOverride = this._checkingOverride.asReadonly();
  private readonly _verifyContent = signal(this.draft?.verifyContent ?? false);
  readonly verifyContent = this._verifyContent.asReadonly();
  private readonly _deleteEmptyFolders = signal(this.draft?.deleteEmptyFolders ?? true);
  readonly deleteEmptyFolders = this._deleteEmptyFolders.asReadonly();
  private readonly _numberCollisions = signal(this.draft?.numberCollisions ?? false);
  readonly numberCollisions = this._numberCollisions.asReadonly();

  private readonly _preview = signal<PreviewResult | null>(null);
  readonly preview = this._preview.asReadonly();
  private readonly _plan = signal<PlanView | null>(null);
  readonly plan = this._plan.asReadonly();
  private readonly _planId = signal('');
  readonly planId = this._planId.asReadonly();
  /** The file whose TMDB picker is open. */
  private readonly _matching = signal<MatchContext | null>(null);
  readonly matching = this._matching.asReadonly();
  private readonly _loadingPreview = signal(false);
  readonly loadingPreview = this._loadingPreview.asReadonly();
  private readonly _preparing = signal(false);
  readonly preparing = this._preparing.asReadonly();
  private readonly _executing = signal(false);
  readonly executing = this._executing.asReadonly();
  private readonly _error = signal('');
  readonly error = this._error.asReadonly();
  private readonly _suggesting = signal(false);
  readonly suggesting = this._suggesting.asReadonly();
  private readonly _searchType = signal('movie');
  readonly searchType = this._searchType.asReadonly();
  private readonly _suggestProgress = signal({ done: 0, total: 0 });
  readonly suggestProgress = this._suggestProgress.asReadonly();
  private readonly suggestions = signal<MatchSuggestion[]>([]);

  readonly stages = computed((): WizardStep[] => [
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
            id: 'matching' as const,
            title: 'Match TMDB metadata',
            description: 'Review and confirm the movie or episode metadata used to name each file.',
          },
        ]
      : []),
    ...(this.mode() === 'simple'
      ? [
          {
            id: 'review' as const,
            title: 'Review and rename',
            description:
              'Check the proposed names, then confirm. Nothing is renamed until you click the rename button.',
          },
        ]
      : [
          {
            id: 'preview' as const,
            title: 'Review filenames',
            description:
              'Compare the original and proposed paths. Resolve any conflicts before preparing your plan.',
          },
          {
            id: 'confirmation' as const,
            title: 'Confirm renaming',
            description:
              'Review the exact prepared changes. Only your explicit confirmation can rename files.',
          },
        ]),
  ]);
  /** Whether Simple's review shows the prepared plan instead of the preview. */
  readonly showingPlan = computed(() => this.preparing() || !!this.plan() || !!this.planId());
  readonly screen = computed<WizardScreen>(() => {
    const stage = this.stage();
    if (stage !== 'review') return stage;
    return this.showingPlan() ? 'confirmation' : 'preview';
  });
  readonly stageIndex = computed(() =>
    this.stages().findIndex((stage) => stage.id === this.stage()),
  );
  readonly validCollection = computed(
    () => this.files().length > 0 && this.files().length <= this.maxFiles(),
  );
  /** The Simple preset that builds the current steps, if any. */
  readonly simplePreset = computed(() => matchSimplePreset(this.plugins())?.preset ?? null);
  readonly tmdbPlugins = computed(() =>
    this.plugins().filter((plugin) => plugin.enabled && plugin.typeId === 'tmdb'),
  );
  /** Related file paths and the video each follows, as the server will pair them. */
  readonly companions = computed(() =>
    this.includeRelated()
      ? detectCompanions(this.files().map((file) => file.path))
      : new Map<string, string>(),
  );
  readonly matchRows = computed(() =>
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
  readonly metadataPending = computed(() => this.preview()?.files.some(awaitingMetadata));
  readonly unmatchedFiles = computed(
    () => this.preview()?.files.filter(awaitingMetadata).length ?? 0,
  );
  /** Verified high-confidence proposals; lower ones are reviewed and accepted one by one. */
  readonly pendingMatches = computed(() =>
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
      this.entered.complete();
    });
  }

  /** The collection or the related-files choice changed: everything computed from it is stale. */
  setCollection(files: RenamerFile[], includeRelated: boolean): void {
    this._files.set(files);
    this._includeRelated.set(includeRelated);
    this.invalidate();
  }

  next(): void {
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

  back(): void {
    if (this.preparing() || this.executing()) return;
    this.leaveForEarlierStage();
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

  /** Jumps straight to a completed stage from the progress list. */
  goToStep(index: number): void {
    const target = this.stages()[index];
    if (!target || index >= this.stageIndex() || this.preparing() || this.executing()) return;
    this.leaveForEarlierStage();
    this.goTo(target.id);
  }

  /** Stops any pending TMDB suggestions and closes the picker before moving backwards. */
  private leaveForEarlierStage(): void {
    this.suggestionRequest?.unsubscribe();
    this._suggesting.set(false);
    this.pendingSuggestions = false;
    this._matching.set(null);
  }

  canReview(): boolean {
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
      this._stage.set(target);
      this.entered.next();
    }
    if (mode === 'advanced' && stage !== 'files' && !this.catalogLoaded() && !this.catalogLoading)
      this.loadCatalog();
  }

  /** The user discarded the steps to switch to Simple: start the pipeline afresh. */
  private discardSteps(): void {
    this._plugins.set([]);
    this.applySimpleRunOptions();
    this._overrides.set([]);
    this.invalidate();
  }

  private applySimpleRunOptions(): void {
    if (this.numberCollisions() !== simpleRunOptions.numberCollisions) {
      this._numberCollisions.set(simpleRunOptions.numberCollisions);
      this._overrides.set([]);
      this.invalidate();
    }
    this._verifyContent.set(simpleRunOptions.verifyContent);
    this._deleteEmptyFolders.set(simpleRunOptions.deleteEmptyFolders);
  }

  /** A Simple preset or one of its options built new steps. */
  choosePreset(plugins: PluginInstance[]): void {
    this.changePlugins(plugins);
    this.applySimpleRunOptions();
    const mediaType = matchSimplePreset(plugins)?.preset.tmdbMediaType;
    if (mediaType) this._searchType.set(mediaType);
  }

  private goTo(stage: WizardStage): void {
    this._backwards.set(this.stages().findIndex((item) => item.id === stage) < this.stageIndex());
    this._stage.set(stage);
    this.entered.next();
  }

  loadCatalog(): void {
    this.catalogLoading = true;
    this._catalogError.set('');
    this.api
      .catalog()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this._catalog.set(catalog);
          this._catalogLoaded.set(true);
          this.catalogLoading = false;
        },
        error: (error: unknown) => {
          this.catalogLoading = false;
          this._catalogError.set(renameError(error));
        },
      });
  }

  changePlugins(plugins: PluginInstance[]): void {
    for (const plugin of plugins) {
      const previous = this.plugins().find((entry) => entry.instanceId === plugin.instanceId);
      if (previous && previous.options.language !== plugin.options.language) {
        this.pickerStates.delete(plugin.instanceId);
        this.showStates.delete(plugin.instanceId);
      }
    }
    this._plugins.set(plugins);
    this._overrides.set([]);
    this.invalidate();
  }

  /** A loaded preset already replaced the steps through the editor; take over its run options. */
  applyPreset(preset: PresetView): void {
    this._verifyContent.set(preset.verifyContent);
    this._deleteEmptyFolders.set(preset.deleteEmptyFolders);
    this.setNumberCollisions(preset.onCollision === 'AddNumber');
  }

  /** Collision handling changes the computed names, so the preview is taken again. */
  setNumberCollisions(on: boolean): void {
    if (on === this.numberCollisions()) return;
    this._numberCollisions.set(on);
    this._overrides.set([]);
    this.invalidate();
  }

  /** Hashing is decided when the plan is prepared; the names do not change. */
  setVerifyContent(on: boolean): void {
    this._verifyContent.set(on);
  }

  /** Empty folders are removed by the run; the names do not change. */
  setDeleteEmptyFolders(on: boolean): void {
    this._deleteEmptyFolders.set(on);
  }

  refreshPreview(): void {
    clearTimeout(this.debounce);
    this.request?.unsubscribe();
    if (!this.files().length || this.files().length > this.maxFiles()) return;
    this.previewStarted = true;
    const revision = this.revision;
    this._loadingPreview.set(true);
    this._error.set('');
    this.request = this.api
      .preview(this.snapshot())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => {
          if (revision !== this.revision) return;
          this._selections.update((selections) =>
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
          this._preview.set(preview);
          this.previewRevision = revision;
          this._loadingPreview.set(false);
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
          this._loadingPreview.set(false);
          this._error.set(renameError(error));
        },
      });
  }

  canPrepare(): boolean {
    return (
      !!this.preview()?.canPrepare &&
      this.previewRevision === this.revision &&
      !this.loadingPreview() &&
      !this.preparing() &&
      !this.executing() &&
      !!this.preview()?.files.some((file) => !file.unchanged)
    );
  }

  prepare(): void {
    if (!this.canPrepare()) return;
    this.goTo(this.mode() === 'simple' ? 'review' : 'confirmation');
    const revision = this.revision;
    this._preparing.set(true);
    this._plan.set(null);
    this._planId.set('');
    this._error.set('');
    this.preparation = this.api
      .prepare(this.snapshot())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (accepted) => {
          if (revision !== this.revision) return;
          this._planId.set(accepted.id);
          this.pollPlan(accepted.id, revision);
        },
        error: (error: unknown) => {
          if (revision !== this.revision) return;
          this._preparing.set(false);
          this._error.set(renameError(error));
        },
      });
  }

  retryPlan(): void {
    if (this.planId()) this.pollPlan(this.planId(), this.revision);
  }

  canConfirm(): boolean {
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
  overrideTarget(fileId: string, targetPath: string | null): void {
    if (!this.canConfirm()) return;
    const overrides = [
      ...this.overrides().filter((entry) => entry.fileId !== fileId),
      ...(targetPath === null ? [] : [{ fileId, targetPath }]),
    ];
    const revision = this.revision;
    this._checkingOverride.set(true);
    this._error.set('');
    this.request?.unsubscribe();
    this.request = this.api
      .preview({ ...this.snapshot(), overrides })
      .pipe(
        finalize(() => this._checkingOverride.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (preview) => {
          if (revision !== this.revision) return;
          const issue = preview.files.find((file) => file.id === fileId)?.issues[0];
          if (issue) {
            this._error.set(`The new name cannot be used: ${issue.message}`);
            return;
          }
          this._overrides.set(overrides);
          this.invalidate(true);
          this._preview.set(preview);
          this.previewRevision = this.revision;
          if (this.canPrepare()) this.prepare();
          else this.goTo(this.reviewStage());
        },
        error: (error: unknown) => {
          if (revision === this.revision) this._error.set(renameError(error));
        },
      });
  }

  confirm(): void {
    const plan = this.plan();
    if (!plan || !this.canConfirm()) return;
    const revision = this.revision;
    this._executing.set(true);
    this._error.set('');
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
          this._executing.set(false);
          if (revision === this.revision) this._error.set(renameError(error));
        },
      });
  }

  /** Opens the TMDB picker for one file, starting from its last search or the show it shares. */
  match(fileId: string, step: StepTrace): void {
    const plugin = this.plugins().find((entry) => entry.instanceId === step.instanceId);
    if (!plugin || this.previewRevision !== this.revision) return;
    this.matchingRevision = this.revision;
    const saved = this.pickerStates.get(plugin.instanceId)?.get(fileId);
    const show = this.showStates.get(plugin.instanceId);
    const inferred = tmdbFilename(step.inputPath, this.searchType());
    this._matching.set({
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

  /** Closes the TMDB picker without accepting a match. */
  closeMatch(): void {
    this._matching.set(null);
  }

  rememberMatch(state: TmdbPickerState): void {
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

  acceptMatch(selection: MetadataSelection): void {
    if (this.matchingRevision !== this.revision) return;
    const match = this.pickerStates.get(selection.instanceId)?.get(selection.fileId)?.selected;
    if (match?.confirmationToken === selection.confirmationToken) {
      this.setSuggestion({ ...selection, match, error: '' });
    }
    this._selections.update((selections) => [
      ...selections.filter(
        (entry) => entry.fileId !== selection.fileId || entry.instanceId !== selection.instanceId,
      ),
      selection,
    ]);
    this._matching.set(null);
    this._overrides.set([]);
    this.invalidate();
  }

  suggestMatches(mediaType: string): void {
    if (
      this.suggesting() ||
      this.preparing() ||
      this.executing() ||
      !this.files().length ||
      this.files().length > this.maxFiles()
    )
      return;
    this._searchType.set(mediaType);
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
    this._suggesting.set(true);
    this._suggestProgress.set({ done: 0, total: rows.length });
    // One request per file: the server parses the name, searches, scores and verifies, and its
    // TMDB cache makes repeated searches for the episodes of one show cheap.
    this.suggestionRequest = from(rows)
      .pipe(
        concatMap((row) =>
          this.suggestFor(row, mediaType).pipe(
            finalize(() => this._suggestProgress.update((p) => ({ ...p, done: p.done + 1 }))),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ complete: () => this._suggesting.set(false) });
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

  acceptSuggestion(suggestion: MatchSuggestion): void {
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

  acceptAllMatches(): void {
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
    this._selections.update((selections) => [
      ...selections.filter(
        (selection) =>
          !accepted.some(
            (entry) =>
              entry.fileId === selection.fileId && entry.instanceId === selection.instanceId,
          ),
      ),
      ...accepted,
    ]);
    this._overrides.set([]);
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

  /** The pipeline request the server previews and prepares. */
  snapshot(): PipelineRequest {
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
    this._suggesting.set(false);
    this.pendingSuggestions = false;
    clearTimeout(this.debounce);
    this._preview.set(null);
    this._plan.set(null);
    this._planId.set('');
    this._matching.set(null);
    this._loadingPreview.set(false);
    this._preparing.set(false);
    this._error.set('');
    this.executeOperation = '';
    const files = new Set(this.files().map((file) => file.path));
    this._overrides.update((overrides) => overrides.filter((entry) => files.has(entry.fileId)));
    const plugins = new Map(
      this.plugins()
        .filter((plugin) => plugin.typeId === 'tmdb')
        .map((plugin) => [plugin.instanceId, tmdbOptionsHash(plugin.options)]),
    );
    this._selections.update((selections) =>
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
    this._preparing.set(true);
    this._error.set('');
    this.polling = refreshOn(this.events.watch('Plan', id), () => this.api.plan(id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (plan) => {
          if (revision !== this.revision) return;
          this._plan.set(plan);
          if (plan.status !== 'Preparing') {
            this._preparing.set(false);
            this.planRevision = revision;
            this.polling?.unsubscribe();
          }
        },
        error: (error: unknown) => {
          if (revision !== this.revision) return;
          this._preparing.set(false);
          this._error.set(renameError(error));
        },
      });
  }
}
