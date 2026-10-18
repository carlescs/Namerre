import { Component, computed, input, output } from '@angular/core';
import { PlanView } from '../../rename-api';

@Component({
  imports: [],
  selector: 'app-rename-confirmation',
  styleUrl: './rename-confirmation.component.scss',
  templateUrl: './rename-confirmation.component.html',
})
export class RenameConfirmationComponent {
  readonly plan = input<PlanView | null>(null);
  readonly fileCount = input(0);
  readonly preparing = input(false);
  readonly executing = input(false);
  readonly canConfirm = input(false);
  readonly canRetry = input(false);
  readonly confirmRequested = output<void>();
  readonly retryRequested = output<void>();
  protected readonly changedCount = computed(
    () => this.plan()?.files.filter((file) => file.sourcePath !== file.targetPath).length ?? 0,
  );
}
