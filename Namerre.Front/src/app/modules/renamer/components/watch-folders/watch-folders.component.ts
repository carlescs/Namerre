import { DatePipe, PercentPipe } from '@angular/common';
import { Component, DestroyRef, inject, type OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, interval, startWith, switchMap } from 'rxjs';
import { RenameApi, renameError, type WatchFolderStatus } from '../../rename-api';

/** How often the panel re-reads the folders' state. */
const REFRESH_MS = 30_000;

/** Shows the configured watch folders with their problems, and pauses, resumes or retries them. */
@Component({
  imports: [DatePipe, PercentPipe],
  selector: 'app-watch-folders',
  templateUrl: './watch-folders.component.html',
})
export class WatchFoldersComponent implements OnInit {
  private readonly api = inject(RenameApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly folders = signal<WatchFolderStatus[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void {
    interval(REFRESH_MS)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.api.watchFolders().pipe(
            // A failed read keeps the last state; the next refresh tries again.
            catchError((error: unknown) => {
              this.error.set(renameError(error));
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((folders) => {
        this.error.set('');
        this.folders.set(folders);
      });
  }

  protected togglePause(folder: WatchFolderStatus): void {
    this.update(this.api.setWatchFolderPaused(folder.name, !folder.paused));
  }

  protected retry(folder: WatchFolderStatus, path: string): void {
    this.update(this.api.retryWatchedFile(folder.name, path));
  }

  private update(request: ReturnType<RenameApi['setWatchFolderPaused']>): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.folders.update((folders) =>
          folders.map((folder) => (folder.name === updated.name ? updated : folder)),
        );
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(renameError(error));
      },
    });
  }
}
