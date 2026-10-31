import { afterNextRender, Component, type ElementRef, inject, viewChild } from '@angular/core';
import { WorkflowMode } from './workflow-mode';
import { openModal } from './workflow-onboarding';

/** Asks before a switch to Simple discards steps that no Simple preset can show. */
@Component({
  selector: 'app-workflow-mode-warning',
  styleUrl: './workflow-dialog.scss',
  templateUrl: './workflow-mode-warning.html',
})
export class WorkflowModeWarning {
  protected readonly workflow = inject(WorkflowMode);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    afterNextRender(() => openModal(this.dialog().nativeElement));
  }

  protected discard(): void {
    this.dialog().nativeElement.close();
    this.workflow.confirm();
  }

  protected cancel(): void {
    this.dialog().nativeElement.close();
    this.workflow.cancel();
  }
}
