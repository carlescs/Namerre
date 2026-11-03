import { Component, inject } from '@angular/core';
import { ConfirmDialog } from '../dialogs/confirm-dialog';
import { WorkflowMode } from './workflow-mode';

/** Asks before a switch to Simple discards steps that no Simple preset can show. */
@Component({
  imports: [ConfirmDialog],
  selector: 'app-workflow-mode-warning',
  templateUrl: './workflow-mode-warning.html',
})
export class WorkflowModeWarning {
  protected readonly workflow = inject(WorkflowMode);
}
