import { LowerCasePipe } from '@angular/common';
import { Component, inject, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faTrashCan } from '@fortawesome/free-solid-svg-icons';
import { Auth } from '../../../../auth/auth';
import { isSettledRun, type RunView } from '../../rename-models';
import { kindLabel, runTitle } from '../run-history/run-display';

/** The administrator's trash button for one run record; only settled runs can be deleted. */
@Component({
  imports: [FaIconComponent, LowerCasePipe],
  selector: 'app-run-delete-button',
  templateUrl: './run-delete-button.component.html',
  host: { class: 'contents' },
})
export class RunDeleteButtonComponent {
  readonly run = input.required<RunView>();
  readonly disabled = input(false);
  readonly requested = output<string>();
  protected readonly auth = inject(Auth);
  protected readonly isSettledRun = isSettledRun;
  protected readonly kindLabel = kindLabel;
  protected readonly runTitle = runTitle;
  protected readonly trashIcon = faTrashCan;
}
