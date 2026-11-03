import { afterNextRender, Component, type ElementRef, inject, viewChild } from '@angular/core';
import { openModal } from '../dialogs/modal';
import { WorkflowMode, type WorkflowModeValue } from './workflow-mode';

/** First-run prompt asking which workflow mode to use in this browser. */
@Component({
  selector: 'app-workflow-onboarding',
  styleUrl: './workflow-dialog.scss',
  templateUrl: './workflow-onboarding.html',
})
export class WorkflowOnboarding {
  private readonly workflow = inject(WorkflowMode);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    afterNextRender(() => openModal(this.dialog().nativeElement));
  }

  protected choose(mode: WorkflowModeValue): void {
    this.dialog().nativeElement.close();
    void this.workflow.request(mode);
  }

  protected dismiss(): void {
    this.dialog().nativeElement.close();
    this.workflow.dismiss();
  }
}
