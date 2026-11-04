import { Component, computed, input, linkedSignal, output, untracked } from '@angular/core';
import type { SelectionPreview } from '../../rename-models';
import { fileCount } from '../run-history/run-display';

/** What the reviewed undo sends: only the safe files, and whether to remove emptied folders. */
export interface UndoConfirmation {
  fileIds: string[];
  deleteEmptyFolders: boolean;
}

/**
 * The checked undo of selected files: which are safe and which stay untouched, and the folder
 * cleanup choice. The parent sends the undo and owns `busy`.
 */
@Component({
  selector: 'app-run-undo-review',
  templateUrl: './run-undo-review.component.html',
})
export class RunUndoReviewComponent {
  readonly review = input.required<SelectionPreview>();
  /** The rename's own cleanup choice; each new review starts from it. */
  readonly runDeleteEmptyFolders = input(false);
  readonly busy = input(false);
  readonly confirmed = output<UndoConfirmation>();
  protected readonly deleteEmptyFolders = linkedSignal({
    source: this.review,
    computation: () => untracked(this.runDeleteEmptyFolders),
  });
  protected readonly safeIds = computed(() =>
    this.review()
      .files.filter((file) => file.safe)
      .map((file) => file.id),
  );
  protected readonly fileCount = fileCount;

  protected confirm(): void {
    if (!this.safeIds().length || this.busy()) return;
    this.confirmed.emit({
      fileIds: this.safeIds(),
      deleteEmptyFolders: this.deleteEmptyFolders(),
    });
  }
}
