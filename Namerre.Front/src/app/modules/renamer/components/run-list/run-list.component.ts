import { DatePipe, LowerCasePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faTrashCan } from '@fortawesome/free-solid-svg-icons';
import type { Subscription } from 'rxjs';
import { Auth } from '../../../../auth/auth';
import { ConfirmDialog } from '../../../../dialogs/confirm-dialog';
import { RenameApi, renameError } from '../../rename-api';
import { RenameEvents, refreshOn } from '../../rename-events';
import {
  type DeletableRuns,
  isSettledRun,
  MAX_RUNS_PER_DELETION,
  type RunPage,
  watchFolderOrigin,
} from '../../rename-models';
import { RunDeleteButtonComponent } from '../run-delete-button/run-delete-button.component';
import { RunDeleteDialogComponent } from '../run-delete-dialog/run-delete-dialog.component';
import {
  fileCount,
  historyEntries,
  kindLabel,
  retain,
  runStatusLabel,
  runTitle,
  statusClasses,
} from '../run-history/run-display';
import { WatchFoldersComponent } from '../watch-folders/watch-folders.component';

/** The paged rename history, with single and bulk deletion of settled runs for administrators. */
@Component({
  imports: [
    ConfirmDialog,
    DatePipe,
    FaIconComponent,
    LowerCasePipe,
    RouterLink,
    RunDeleteButtonComponent,
    RunDeleteDialogComponent,
    WatchFoldersComponent,
  ],
  selector: 'app-run-list',
  styleUrl: '../../workflow.scss',
  templateUrl: './run-list.component.html',
})
export class RunListComponent {
  protected readonly auth = inject(Auth);
  private readonly api = inject(RenameApi);
  private readonly events = inject(RenameEvents);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly history = signal<RunPage | null>(null);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly pendingDelete = signal('');
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal('');
  /** Runs ticked for bulk deletion, kept across pages. */
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
  protected readonly isSettledRun = isSettledRun;
  protected readonly fileCount = fileCount;
  protected readonly kindLabel = kindLabel;
  protected readonly watchFolderOrigin = watchFolderOrigin;
  protected readonly runTitle = runTitle;
  protected readonly statusClasses = statusClasses;
  protected readonly runStatusLabel = runStatusLabel;
  protected readonly trashIcon = faTrashCan;
  private polling?: Subscription;
  private loadRevision = 0;
  private deleteRequest?: Subscription;
  private candidatesRequest?: Subscription;

  constructor() {
    this.refresh();
    this.destroyRef.onDestroy(() => {
      this.polling?.unsubscribe();
      this.deleteRequest?.unsubscribe();
      this.candidatesRequest?.unsubscribe();
    });
  }

  protected refresh(): void {
    this.polling?.unsubscribe();
    this.error.set('');
    this.loading.set(true);
    const revision = ++this.loadRevision;
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

  protected changePage(direction: number): void {
    this.page.update((page) => Math.max(1, page + direction));
    this.cancelDelete();
    this.refresh();
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
          this.reloadAfterDeletion([id]);
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
}
