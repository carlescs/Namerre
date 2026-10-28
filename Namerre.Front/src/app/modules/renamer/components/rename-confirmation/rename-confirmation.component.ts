import { Component, computed, input, output } from '@angular/core';
import type { PlanView } from '../../rename-api';
import { NameChangeComponent } from '../name-change/name-change.component';

@Component({
  imports: [NameChangeComponent],
  selector: 'app-rename-confirmation',
  styleUrl: './rename-confirmation.component.scss',
  templateUrl: './rename-confirmation.component.html',
})
export class RenameConfirmationComponent {
  readonly plan = input<PlanView | null>(null);
  readonly fileCount = input(0);
  readonly verifyContent = input(true);
  readonly preparing = input(false);
  readonly executing = input(false);
  readonly canConfirm = input(false);
  readonly canRetry = input(false);
  readonly confirmRequested = output<void>();
  readonly retryRequested = output<void>();
  protected readonly changedCount = computed(
    () => this.plan()?.files.filter((file) => file.sourcePath !== file.targetPath).length ?? 0,
  );
  protected readonly changedLabel = computed(() => (this.changedCount() === 1 ? 'file' : 'files'));
  protected readonly completed = computed(() => this.plan()?.completedFiles ?? 0);
  protected readonly percent = computed(() =>
    this.fileCount() > 0 ? Math.min(100, (this.completed() / this.fileCount()) * 100) : 0,
  );
}
