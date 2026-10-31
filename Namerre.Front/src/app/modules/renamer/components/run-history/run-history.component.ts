import { DatePipe, LowerCasePipe, NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  type ElementRef,
  Injector,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faTrashCan } from '@fortawesome/free-solid-svg-icons';
import { BehaviorSubject, catchError, EMPTY, exhaustMap, type Subscription, timer } from 'rxjs';
import {
  type DeletableRuns,
  isSettledRun,
  MAX_RUNS_PER_DELETION,
  operationId,
  type PlanView,
  type RecoveryPreview,
  RenameApi,
  type RunPage,
  type RunView,
  renameError,
  type SelectionPreview,
  type SelectionRequest,
} from '../../rename-api';
import { RenameEvents, refreshOn } from '../../rename-events';
import {
  fileCount,
  historyEntries,
  kindLabel,
  optionEntries,
  pluginName,
  runStatusLabel,
  runTitle,
  showsStaging,
  splitPath,
  statusClasses,
  statusLabel,
} from './run-display';

@Component({
  imports: [RouterLink, DatePipe, FaIconComponent, LowerCasePipe, NgTemplateOutlet],
  selector: 'app-run-history',
  styleUrl: './run-history.component.scss',
  templateUrl: './run-history.component.html',
})
export class RunHistoryComponent {
  private readonly api = inject(RenameApi);
  private readonly events = inject(RenameEvents);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('pageHeading');
  protected readonly run = signal<RunView | null>(null);
  protected readonly history = signal<RunPage | null>(null);
  protected readonly page = signal(1);
  protected readonly runId = signal('');
  protected readonly selected = signal<Set<string>>(new Set());
  protected readonly selectableIds = computed(() => {
    const run = this.run();
    if (run?.status !== 'Completed' || run.kind !== 'Rename') return [];
    return run.files.filter((file) => file.status === 'Completed').map((file) => file.id);
  });
  protected readonly someSelected = computed(() =>
    this.selectableIds().some((id) => this.selected().has(id)),
  );
  protected readonly allSelected = computed(() => {
    const ids = this.selectableIds();
    const selection = this.selected();
    return ids.length > 0 && ids.every((id) => selection.has(id));
  });
  protected readonly rollback = signal<SelectionPreview | null>(null);
  /** Cleanup choice for the reviewed undo; starts from the rename's own choice. */
  protected readonly rollbackDeleteEmptyFolders = signal(false);
  protected readonly recovery = signal<RecoveryPreview | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly checkingRollback = signal(0);
  protected readonly checkedFiles = signal(0);
  protected readonly error = signal('');
  protected readonly actionError = signal('');
  protected readonly savedPlan = signal<PlanView | null>(null);
  protected readonly planError = signal('');
  protected readonly loadingPlan = signal(false);
  protected readonly pendingDelete = signal('');
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal('');
  /** Runs ticked in the history list for bulk deletion, kept across pages. */
  protected readonly chosenRuns = signal<Set<string>>(new Set());
  protected readonly pendingBulkDelete = signal(false);
  /** Set while the choice holds every deletable run, as last read from the server. */
  protected readonly everyPage = signal<DeletableRuns | null>(null);
  protected readonly selectingEveryPage = signal(false);
  protected readonly selectionError = signal('');
  protected readonly maxRunsPerDeletion = MAX_RUNS_PER_DELETION;
  protected readonly tooManyRuns = computed(() => this.chosenRuns().size > MAX_RUNS_PER_DELETION);
  protected readonly deletableRunIds = computed(() =>
    (this.history()?.items ?? []).filter(isSettledRun).map((run) => run.id),
  );
  protected readonly someRunsChosen = computed(() =>
    this.deletableRunIds().some((id) => this.chosenRuns().has(id)),
  );
  protected readonly allRunsChosen = computed(() => {
    const ids = this.deletableRunIds();
    const chosen = this.chosenRuns();
    return ids.length > 0 && ids.every((id) => chosen.has(id));
  });
  protected readonly entries = computed(() => historyEntries(this.history()?.items ?? []));
  protected readonly stagingVisible = computed(() => {
    const run = this.run();
    return !!run && showsStaging(run);
  });
  protected readonly isSettledRun = isSettledRun;
  protected readonly fileCount = fileCount;
  protected readonly kindLabel = kindLabel;
  protected readonly optionEntries = optionEntries;
  protected readonly pluginName = pluginName;
  protected readonly runTitle = runTitle;
  protected readonly splitPath = splitPath;
  protected readonly statusClasses = statusClasses;
  protected readonly statusLabel = statusLabel;
  protected readonly runStatusLabel = runStatusLabel;
  protected readonly trashIcon = faTrashCan;
  /** Address of the CSV or JSON export of a run's files (source, target, status and rollback). */
  protected exportUrl(id: string, format: 'csv' | 'json'): string {
    return this.api.exportUrl(id, format);
  }
  private polling?: Subscription;
  private action?: Subscription;
  private checkProgress?: Subscription;
  private revision = 0;
  private selectionRequest: SelectionRequest | null = null;
  private recoveryOperation = '';
  private runSignature = '';
  private planRequest?: Subscription;
  private loadRevision = 0;
  private deleteRequest?: Subscription;
  private candidatesRequest?: Subscription;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.runId.set(params.get('id') ?? '');
      this.run.set(null);
      this.history.set(null);
      this.selected.set(new Set());
      this.clearRuns();
      this.planRequest?.unsubscribe();
      this.savedPlan.set(null);
      this.planError.set('');
      this.loadingPlan.set(false);
      this.cancelDelete();
      this.invalidateApproval();
      this.refresh();
      afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
    });
    this.destroyRef.onDestroy(() => {
      this.polling?.unsubscribe();
      this.action?.unsubscribe();
      this.checkProgress?.unsubscribe();
      this.planRequest?.unsubscribe();
      this.deleteRequest?.unsubscribe();
      this.candidatesRequest?.unsubscribe();
    });
  }

  protected refresh(): void {
    this.polling?.unsubscribe();
    this.error.set('');
    this.loading.set(true);
    const id = this.runId();
    const revision = ++this.loadRevision;
    if (id) {
      // Settled runs keep listening: a rollback or recovery (possibly from another tab) can still
      // change their files. Only the fallback poll stops once the run is no longer active.
      const active = new BehaviorSubject(true);
      this.polling = refreshOn(this.events.watch('Run', id, active), () => this.api.run(id))
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (run) => {
            if (revision !== this.loadRevision) return;
            const signature = JSON.stringify({ status: run.status, files: run.files });
            if (this.runSignature !== signature) {
              this.runSignature = signature;
              this.invalidateApproval();
            }
            this.run.set(run);
            const selectable = new Set(this.selectableIds());
            this.selected.update((selection) => retain(selection, (id) => selectable.has(id)));
            this.loading.set(false);
            active.next(['Queued', 'Running', 'RecoveryQueued', 'Recovering'].includes(run.status));
          },
          error: (error: unknown) => {
            if (revision !== this.loadRevision) return;
            this.loading.set(false);
            this.error.set(renameError(error));
          },
        });
    } else {
      // The list follows pushed changes but does not poll: a stale page is harmless.
      this.polling = refreshOn(this.events.watch('Run', undefined, false), () =>
        this.api.runs(this.page()),
      )
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (history) => {
            if (revision !== this.loadRevision) return;
            this.history.set(history);
            // Choices from other pages are kept; only runs seen here as no longer deletable drop out.
            const blocked = new Set(history.items.filter((r) => !isSettledRun(r)).map((r) => r.id));
            this.chosenRuns.update((chosen) => retain(chosen, (id) => !blocked.has(id)));
            this.loading.set(false);
          },
          error: (error: unknown) => {
            if (revision !== this.loadRevision) return;
            this.error.set(renameError(error));
            this.loading.set(false);
          },
        });
    }
  }

  protected inspectPlan(): void {
    const id = this.run()?.planId;
    if (!id || this.savedPlan() || this.loadingPlan()) return;
    this.loadingPlan.set(true);
    this.planError.set('');
    const runId = this.runId();
    this.planRequest = this.api
      .plan(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (plan) => {
          if (runId !== this.runId()) return;
          this.savedPlan.set(plan);
          this.loadingPlan.set(false);
        },
        error: (error: unknown) => {
          if (runId !== this.runId()) return;
          this.loadingPlan.set(false);
          this.planError.set(renameError(error));
        },
      });
  }

  protected changePage(direction: number): void {
    this.page.update((page) => Math.max(1, page + direction));
    this.cancelDelete();
    this.refresh();
  }

  /** Selecting a video for undo selects its related files too; they can still be toggled alone. */
  protected toggle(id: string): void {
    const selectable = new Set(this.selectableIds());
    const related = (this.run()?.files ?? [])
      .filter((file) => file.companionOf === id && selectable.has(file.id))
      .map((file) => file.id);
    this.selected.update((selection) => {
      const next = new Set(selection);
      const select = !next.has(id);
      for (const entry of [id, ...related]) {
        if (select) next.add(entry);
        else next.delete(entry);
      }
      return next;
    });
    this.invalidateApproval();
  }

  protected toggleAll(): void {
    if (this.busy()) return;
    this.selected.set(this.allSelected() ? new Set() : new Set(this.selectableIds()));
    this.invalidateApproval();
  }

  protected previewRollback(): void {
    if (!this.selected().size || this.busy() || this.run()?.status !== 'Completed') return;
    this.invalidateApproval();
    const revision = this.revision;
    const request = { fileIds: [...this.selected()], operationId: operationId() };
    this.selectionRequest = request;
    this.busy.set(true);
    this.checkingRollback.set(request.fileIds.length);
    this.watchCheckProgress(request.operationId, revision);
    this.action = this.api
      .rollbackPreview(this.runId(), request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => {
          if (revision !== this.revision) return;
          this.rollbackDeleteEmptyFolders.set(this.run()?.deleteEmptyFolders ?? false);
          this.rollback.set(preview);
          this.busy.set(false);
          this.stopCheckProgress();
        },
        error: (error: unknown) => this.failAction(error, revision),
      });
  }

  protected safeCount(): number {
    return this.rollback()?.files.filter((file) => file.safe).length ?? 0;
  }

  protected confirmRollback(): void {
    if (!this.selectionRequest || !this.safeCount() || this.busy()) return;
    const revision = this.revision;
    this.busy.set(true);
    this.actionError.set('');
    const request = {
      ...this.selectionRequest,
      fileIds:
        this.rollback()
          ?.files.filter((file) => file.safe)
          .map((file) => file.id) ?? [],
      deleteEmptyFolders: this.rollbackDeleteEmptyFolders(),
    };
    this.action = this.api
      .rollback(this.runId(), request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (accepted) => {
          void this.router.navigate(['/runs', accepted.id]);
        },
        error: (error: unknown) => this.failAction(error, revision),
      });
  }

  protected previewRecovery(): void {
    if (this.busy() || this.run()?.status !== 'RecoveryRequired') return;
    this.invalidateApproval();
    const revision = this.revision;
    this.recoveryOperation = operationId();
    this.busy.set(true);
    this.action = this.api
      .recoveryPreview(this.runId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => {
          if (revision !== this.revision) return;
          this.recovery.set(preview);
          this.busy.set(false);
        },
        error: (error: unknown) => this.failAction(error, revision),
      });
  }

  protected confirmRecovery(): void {
    const recovery = this.recovery();
    if (!recovery?.canRecover || this.busy()) return;
    const revision = this.revision;
    this.busy.set(true);
    this.actionError.set('');
    this.action = this.api
      .recover(this.runId(), recovery.action, this.recoveryOperation)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (accepted) => {
          this.busy.set(false);
          this.recovery.set(null);
          if (accepted.id === this.runId()) this.refresh();
          else void this.router.navigate(['/runs', accepted.id]);
        },
        error: (error: unknown) => this.failAction(error, revision),
      });
  }

  protected toggleRun(id: string): void {
    if (this.deleting()) return;
    this.everyPage.set(null);
    this.choose((chosen) => {
      const next = new Set(chosen);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  /** Ticks or unticks every deletable run on this page, leaving choices on other pages alone. */
  protected toggleAllRuns(): void {
    if (this.deleting()) return;
    const ids = this.deletableRunIds();
    const select = !this.allRunsChosen();
    if (!select) this.everyPage.set(null);
    this.choose((chosen) => {
      const next = new Set(chosen);
      for (const id of ids) {
        if (select) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  }

  protected selectEveryPage(): void {
    if (this.deleting() || this.pendingBulkDelete() || this.selectingEveryPage()) return;
    this.selectingEveryPage.set(true);
    this.selectionError.set('');
    this.candidatesRequest = this.api
      .deletableRuns()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (candidates) => {
          this.selectingEveryPage.set(false);
          this.chosenRuns.set(new Set(candidates.ids));
          this.everyPage.set(candidates);
        },
        error: (error: unknown) => {
          this.selectingEveryPage.set(false);
          this.selectionError.set(renameError(error));
        },
      });
  }

  protected clearRuns(): void {
    this.everyPage.set(null);
    this.choose(() => new Set());
  }

  /**
   * Applies a manual selection change. It supersedes a pending "every page" read, so a late
   * answer cannot bring back runs the user just unticked, and an emptied selection closes the
   * bulk confirmation instead of reviving it with the next tick.
   */
  private choose(update: (chosen: Set<string>) => Set<string>): void {
    this.candidatesRequest?.unsubscribe();
    this.selectingEveryPage.set(false);
    this.selectionError.set('');
    this.chosenRuns.update(update);
    if (!this.chosenRuns().size) this.pendingBulkDelete.set(false);
  }

  protected requestDelete(id: string): void {
    if (this.deleting()) return;
    this.pendingBulkDelete.set(false);
    this.pendingDelete.set(id);
    this.deleteError.set('');
  }

  protected requestBulkDelete(): void {
    const size = this.chosenRuns().size;
    if (this.deleting() || this.selectingEveryPage() || !size || this.tooManyRuns()) return;
    this.pendingDelete.set('');
    this.pendingBulkDelete.set(true);
    this.deleteError.set('');
  }

  protected cancelDelete(): void {
    this.deleteRequest?.unsubscribe();
    this.pendingDelete.set('');
    this.pendingBulkDelete.set(false);
    this.deleting.set(false);
    this.deleteError.set('');
  }

  protected confirmDelete(): void {
    const id = this.pendingDelete();
    if (!id || this.deleting()) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.deleteRequest = this.api
      .deleteRun(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.pendingDelete.set('');
          this.deleting.set(false);
          if (this.runId()) {
            void this.router.navigate(['/runs']);
          } else {
            this.reloadAfterDeletion([id]);
          }
        },
        error: (error: unknown) => {
          this.deleting.set(false);
          this.deleteError.set(renameError(error));
        },
      });
  }

  protected confirmBulkDelete(): void {
    const ids = [...this.chosenRuns()];
    if (!this.pendingBulkDelete() || !ids.length || this.tooManyRuns() || this.deleting()) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.deleteRequest = this.api
      .deleteRuns(ids)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (deleted) => {
          this.pendingBulkDelete.set(false);
          this.deleting.set(false);
          this.clearRuns();
          this.reloadAfterDeletion(deleted);
        },
        error: (error: unknown) => {
          this.deleting.set(false);
          this.deleteError.set(renameError(error));
        },
      });
  }

  /**
   * Re-reads the list. Deleting runs shown elsewhere shifts every later page by an unknown amount,
   * so that goes back to the first page; otherwise it steps back only when this page emptied.
   */
  private reloadAfterDeletion(deleted: string[]): void {
    const removed = new Set(deleted);
    const items = this.history()?.items ?? [];
    const shown = new Set(items.map((item) => item.id));
    this.chosenRuns.update((chosen) => retain(chosen, (id) => !removed.has(id)));
    if (deleted.some((id) => !shown.has(id))) this.page.set(1);
    else if (this.page() > 1 && items.every((item) => removed.has(item.id)))
      this.page.update((page) => page - 1);
    this.refresh();
  }

  private invalidateApproval(): void {
    this.revision++;
    this.action?.unsubscribe();
    this.rollback.set(null);
    this.recovery.set(null);
    this.selectionRequest = null;
    this.busy.set(false);
    this.stopCheckProgress();
    this.actionError.set('');
  }

  private failAction(error: unknown, revision: number): void {
    if (revision !== this.revision) return;
    this.busy.set(false);
    this.stopCheckProgress();
    this.actionError.set(renameError(error));
  }

  private watchCheckProgress(operation: string, revision: number): void {
    this.checkedFiles.set(0);
    // Progress is best effort: a missed poll leaves the bar where it was until the check answers.
    this.checkProgress = timer(250, 500)
      .pipe(
        exhaustMap(() =>
          this.api.rollbackCheckProgress(this.runId(), operation).pipe(catchError(() => EMPTY)),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((progress) => {
        if (revision !== this.revision || !progress.totalFiles) return;
        this.checkedFiles.update((checked) =>
          Math.max(checked, Math.min(progress.completedFiles, this.checkingRollback())),
        );
      });
  }

  private stopCheckProgress(): void {
    this.checkProgress?.unsubscribe();
    this.checkingRollback.set(0);
    this.checkedFiles.set(0);
  }
}

/** Keeps only the ids that pass `keep`, returning the same set when nothing was dropped. */
function retain(selection: Set<string>, keep: (id: string) => boolean): Set<string> {
  return [...selection].every(keep) ? selection : new Set([...selection].filter(keep));
}
