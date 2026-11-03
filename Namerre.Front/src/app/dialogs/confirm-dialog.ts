import {
  afterNextRender,
  booleanAttribute,
  Component,
  DestroyRef,
  type ElementRef,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { closeModal, openModal } from './modal';

let nextId = 0;

/**
 * A generic modal confirmation: a heading, the projected explanation and two buttons. It opens as
 * soon as it is rendered and closes when removed, so the parent shows it with `@if` and removes it
 * once answered. Cancel (or Escape) emits `cancelled`; the confirm button emits `confirmed`. While
 * `busy`, both buttons and Escape are disabled, so the parent can keep it open to show progress or
 * an error (projected too) for the confirmed action.
 */
@Component({
  selector: 'app-confirm-dialog',
  styleUrl: './confirm-dialog.scss',
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog {
  readonly heading = input.required<string>();
  readonly confirmLabel = input('Confirm');
  readonly cancelLabel = input('Cancel');
  /** Styles the confirm button as destructive; Cancel then takes the initial focus. */
  readonly danger = input(false, { transform: booleanAttribute });
  readonly busy = input(false, { transform: booleanAttribute });
  /** Disables the confirm button, for example while the action is not allowed. */
  readonly confirmDisabled = input(false, { transform: booleanAttribute });
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
  protected readonly id = `confirm-dialog-${nextId++}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    afterNextRender(() => openModal(this.dialog().nativeElement));
    inject(DestroyRef).onDestroy(() => closeModal(this.dialog().nativeElement));
  }

  protected confirm(): void {
    if (this.busy() || this.confirmDisabled()) return;
    this.confirmed.emit();
  }

  protected cancel(): void {
    if (this.busy()) return;
    this.cancelled.emit();
  }
}
