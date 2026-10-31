import { afterNextRender, Component, type ElementRef, inject, viewChild } from '@angular/core';
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

/** Opens a dialog as a modal where supported, else as a plain open dialog. */
export function openModal(dialog: HTMLDialogElement): void {
  if (dialog.open) return;
  if (typeof dialog.showModal === 'function') dialog.showModal();
  else dialog.setAttribute('open', '');
}
