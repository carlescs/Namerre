import { LowerCasePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { ConfirmDialog } from '../../../../dialogs/confirm-dialog';
import type { RunView } from '../../rename-models';
import { kindLabel } from '../run-history/run-display';

/** Asks before one run record is deleted; the parent sends the request and reports its outcome. */
@Component({
  imports: [ConfirmDialog, LowerCasePipe],
  selector: 'app-run-delete-dialog',
  templateUrl: './run-delete-dialog.component.html',
})
export class RunDeleteDialogComponent {
  readonly run = input.required<RunView>();
  readonly deleting = input(false);
  readonly error = input('');
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
  protected readonly kindLabel = kindLabel;
}
