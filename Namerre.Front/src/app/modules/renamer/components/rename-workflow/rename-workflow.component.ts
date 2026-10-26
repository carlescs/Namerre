import {
  afterNextRender,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  computed,
  effect,
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
  Subscription,
  switchMap,
  tap,
} from 'rxjs';
import {
  MatchContext,
  MatchSuggestion,
  MetadataSelection,
  PipelineRequest,
  PlanView,
  PluginDescriptor,
  PluginInstance,
  PreviewResult,
  RenameApi,
  StepTrace,
  TmdbMatch,
  TmdbMatchRow,
  TmdbPickerState,
  operationId,
  awaitingMetadata,
  isMatchingIssue,
  renameError,
} from '../../rename-api';
import { tmdbOptionsHash } from '../../options-hash';
import { RenameEvents, refreshOn } from '../../rename-events';
import { preferredTmdbMatch, tmdbFilename } from '../../tmdb-filename';
import { RenamerFile } from '../../renamer-file';
import { PluginConfigurationComponent } from '../plugin-configuration/plugin-configuration.component';
import { RenameConfirmationComponent } from '../rename-confirmation/rename-confirmation.component';
import { RenamePreviewComponent } from '../rename-preview/rename-preview.component';
import { TmdbMatchingComponent } from '../tmdb-matching/tmdb-matching.component';
import { TmdbPickerComponent } from '../tmdb-picker/tmdb-picker.component';

type WizardStage = 'files' | 'plugins' | 'matching' | 'preview' | 'confirmation';

@Component({
  imports: [
    PluginConfigurationComponent,
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
  readonly fileRemoved = output<string>();
  private readonly api = inject(RenameApi);
  private readonly events = inject(RenameEvents);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
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
    {
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
  ]);
  protected readonly stageIndex = computed(() =>
    this.stages().findIndex((stage) => stage.id === this.stage()),
  );
  protected readonly validCollection = computed(
    () => this.files().length > 0 && this.files().length <= 200,
  );
  protected readonly catalog = signal<PluginDescriptor[]>([]);
  protected readonly catalogLoaded = signal(false);
  private catalogLoading = false;
  protected readonly plugins = signal<PluginInstance[]>([]);
  protected readonly selections = signal<MetadataSelection[]>([]);
  protected readonly verifyContent = signal(false);
  protected readonly deleteEmptyFolders = signal(false);
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
  protected readonly tmdbPlugins = computed(() =>
    this.plugins().filter((plugin) => plugin.enabled && plugin.typeId === 'tmdb'),
  );
  protected readonly matchRows = computed(() =>
    this.files().flatMap((file) =>
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
    ),
  );
  protected readonly metadataPending = computed(() => this.preview()?.files.some(awaitingMetadata));
  protected readonly unmatchedFiles = computed(
    () => this.preview()?.files.filter(awaitingMetadata).length ?? 0,
  );
  protected readonly pendingMatches = computed(() =>
    this.matchRows().flatMap((row): MetadataSelection[] => {
      const suggestion = row.suggestion;
      return row.step?.error &&
        isMatchingIssue(row.step.error.code) &&
        suggestion?.match?.confirmationToken &&
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
      untracked(() => this.invalidate());
    });
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
        this.goTo('plugins');
        if (!this.catalogLoaded() && !this.catalogLoading) this.loadCatalog();
        break;
      case 'plugins':
        if (!this.catalogLoaded()) return;
        this.goTo(this.tmdbPlugins().length ? 'matching' : 'preview');
        if (this.previewRevision !== this.revision) this.refreshPreview();
        break;
      case 'matching':
        if (this.canReview()) this.goTo('preview');
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
        this.goTo('files');
        break;
      case 'matching':
        this.goTo('plugins');
        break;
      case 'preview':
        this.goTo(this.tmdbPlugins().length ? 'matching' : 'plugins');
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
    this.invalidate();
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
    this.goTo('confirmation');
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
            }
          : {
              query: inferred.query,
              mediaType: this.searchType(),
              language: plugin.options.language ?? 'en-US',
              season: inferred.season ?? 1,
              episode: inferred.episode ?? 1,
              matches: [],
              selected: null,
            }),
      reusedShow: !saved && !!show,
    });
  }

  protected rememberMatch(state: TmdbPickerState): void {
    const context = this.matching();
    if (!context || this.matchingRevision !== this.revision) return;
    let states = this.pickerStates.get(context.plugin.instanceId);
    if (!states) {
      states = new Map();
      this.pickerStates.set(context.plugin.instanceId, states);
    }
    states.set(context.fileId, state);
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
    this.invalidate();
  }

  protected suggestMatches(mediaType: string): void {
    if (
      this.suggesting() ||
      this.preparing() ||
      this.executing() ||
      !this.files().length ||
      this.files().length > 200
    )
      return;
    this.searchType.set(mediaType);
    if (this.previewRevision !== this.revision) {
      this.pendingSuggestions = true;
      this.refreshPreview();
      return;
    }
    const rows = this.matchRows().filter(
      (row) =>
        row.step &&
        (!row.step.error || isMatchingIssue(row.step.error.code)) &&
        !this.selections().some(
          (entry) => entry.fileId === row.fileId && entry.instanceId === row.plugin.instanceId,
        ),
    );
    this.suggesting.set(true);
    this.suggestProgress.set({ done: 0, total: rows.length });
    // First-stage searches repeat for every episode of a show; reuse them within this run.
    const searches = new Map<string, TmdbMatch[]>();
    this.suggestionRequest = from(rows)
      .pipe(
        concatMap((row) =>
          this.suggestFor(row, mediaType, searches).pipe(
            finalize(() => this.suggestProgress.update((p) => ({ ...p, done: p.done + 1 }))),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ complete: () => this.suggesting.set(false) });
  }

  private suggestFor(row: TmdbMatchRow, mediaType: string, searches: Map<string, TmdbMatch[]>) {
    return defer(() => {
      const step = row.step!;
      const inferred = tmdbFilename(step.inputPath, mediaType);
      const binding = {
        fileId: row.fileId,
        instanceId: row.plugin.instanceId,
        inputPath: step.inputPath,
        optionsHash: tmdbOptionsHash(row.plugin.options),
      };
      const state: TmdbPickerState = {
        query: inferred.query,
        mediaType,
        language: row.plugin.options.language ?? 'en-US',
        season: inferred.season ?? 1,
        episode: inferred.episode ?? 1,
        matches: [],
        selected: null,
      };
      let states = this.pickerStates.get(row.plugin.instanceId);
      if (!states) {
        states = new Map();
        this.pickerStates.set(row.plugin.instanceId, states);
      }
      states.set(row.fileId, state);
      this.setSuggestion({ ...binding, match: null, error: '' });
      if (
        !inferred.query ||
        (mediaType === 'tv' && (inferred.season === undefined || !inferred.episode))
      ) {
        this.setSuggestion({
          ...binding,
          match: null,
          error:
            'Could not infer the title or episode numbers. Search again and enter them manually.',
        });
        return of(null);
      }
      const searchKey = `${mediaType}|${state.query}|${state.language}`;
      const cached = searches.get(searchKey);
      const search = cached
        ? of({ matches: cached })
        : this.api
            .tmdb({ mediaType, query: state.query, language: state.language })
            .pipe(tap((result) => searches.set(searchKey, result.matches)));
      return search.pipe(
        switchMap((result) => {
          state.matches = result.matches;
          const candidate = preferredTmdbMatch(result.matches, inferred);
          state.selected = candidate ?? null;
          if (!candidate) {
            this.setSuggestion({
              ...binding,
              match: null,
              error: 'No TMDB matches. Search again with a different title.',
            });
            return of(null);
          }
          return this.api
            .tmdb({
              ...binding,
              mediaType,
              query: state.query,
              language: state.language,
              id: candidate.id,
              ...(mediaType === 'tv' ? { season: state.season, episode: state.episode } : {}),
            })
            .pipe(
              tap((verified) => {
                const match = verified.matches.find(
                  (entry) => entry.id === candidate.id && entry.confirmationToken,
                );
                if (match) state.selected = match;
                this.setSuggestion({
                  ...binding,
                  match: match ?? null,
                  error: match
                    ? ''
                    : 'TMDB could not verify this match. Search again to correct it.',
                });
              }),
            );
        }),
        catchError((error: unknown) => {
          this.setSuggestion({ ...binding, match: null, error: renameError(error) });
          return of(null);
        }),
      );
    });
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
    };
  }

  private invalidate(): void {
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
    if (this.stage() === 'confirmation') this.goTo('preview');
    if (this.stage() === 'matching' && !this.tmdbPlugins().length) this.goTo('plugins');
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
