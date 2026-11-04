import { DatePipe, NgTemplateOutlet } from '@angular/common';
import {
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { BehaviorSubject, catchError, EMPTY, exhaustMap, type Subscription, timer } from 'rxjs';
import { operationId, RenameApi, renameError } from '../../rename-api';
import { RenameEvents, refreshOn } from '../../rename-events';
import {
  type RecoveryPreview,
  type RunView,
  type SelectionPreview,
  type SelectionRequest,
  watchFolderOrigin,
} from '../../rename-models';
import { RunDeleteButtonComponent } from '../run-delete-button/run-delete-button.component';
import { RunDeleteDialogComponent } from '../run-delete-dialog/run-delete-dialog.component';
import {
  fileCount,
  kindLabel,
  retain,
  runStatusLabel,
  runTitle,
  showsStaging,
  splitPath,
  statusClasses,
  statusLabel,
} from '../run-history/run-display';
import { RunPlanSnapshotComponent } from '../run-plan-snapshot/run-plan-snapshot.component';

/**
 * One run: what happened to each file, with the reviewed undo of selected files, the reviewed
 * recovery of an interrupted run, its saved settings and its operation log.
 */
@Component({
  imports: [
    DatePipe,
    NgTemplateOutlet,
    RouterLink,
    RunDeleteButtonComponent,
    RunDeleteDialogComponent,
    RunPlanSnapshotComponent,
  ],
  selector: 'app-run-detail',
  styleUrl: './run-detail.component.scss',
  templateUrl: './run-detail.component.html',
})
export class RunDetailComponent {
  readonly runId = input.required<string>();
  private readonly api = inject(RenameApi);
  private readonly events = inject(RenameEvents);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly run = signal<RunView | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
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
  protected readonly busy = signal(false);
  protected readonly checkingRollback = signal(0);
  protected readonly checkedFiles = signal(0);
  protected readonly actionError = signal('');
  protected readonly pendingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal('');
  protected readonly stagingVisible = computed(() => {
    const run = this.run();
    return !!run && showsStaging(run);
  });
  protected readonly fileCount = fileCount;
  protected readonly kindLabel = kindLabel;
  protected readonly watchFolderOrigin = watchFolderOrigin;
  protected readonly runTitle = runTitle;
  protected readonly splitPath = splitPath;
  protected readonly statusClasses = statusClasses;
  protected readonly statusLabel = statusLabel;
  protected readonly runStatusLabel = runStatusLabel;
  private polling?: Subscription;
  private action?: Subscription;
  private checkProgress?: Subscription;
  private revision = 0;
  private selectionRequest: SelectionRequest | null = null;
  private recoveryOperation = '';
  private runSignature = '';
  private loadRevision = 0;
  private deleteRequest?: Subscription;

  constructor() {
    effect(() => {
      this.runId();
      untracked(() => this.open());
    });
    this.destroyRef.onDestroy(() => {
      this.polling?.unsubscribe();
      this.action?.unsubscribe();
      this.checkProgress?.unsubscribe();
      this.deleteRequest?.unsubscribe();
    });
  }

  /** Address of the CSV or JSON export of a run's files (source, target, status and rollback). */
  protected exportUrl(id: string, format: 'csv' | 'json'): string {
    return this.api.exportUrl(id, format);
  }

  /** Starts over for the current run id. */
  private open(): void {
    this.run.set(null);
    this.selected.set(new Set());
    this.cancelDelete();
    this.invalidateApproval();
    this.refresh();
  }

  protected refresh(): void {
    this.polling?.unsubscribe();
    this.error.set('');
    this.loading.set(true);
    const id = this.runId();
    const revision = ++this.loadRevision;
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

  protected requestDelete(): void {
    if (this.deleting()) return;
    this.pendingDelete.set(true);
    this.deleteError.set('');
  }

  protected cancelDelete(): void {
    this.deleteRequest?.unsubscribe();
    this.pendingDelete.set(false);
    this.deleting.set(false);
    this.deleteError.set('');
  }

  protected confirmDelete(): void {
    if (!this.pendingDelete() || this.deleting()) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.deleteRequest = this.api
      .deleteRun(this.runId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.pendingDelete.set(false);
          this.deleting.set(false);
          void this.router.navigate(['/runs']);
        },
        error: (error: unknown) => {
          this.deleting.set(false);
          this.deleteError.set(renameError(error));
        },
      });
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
