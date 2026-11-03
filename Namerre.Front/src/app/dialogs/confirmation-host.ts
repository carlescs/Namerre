import { Component, computed, DestroyRef, inject } from '@angular/core';
import { ConfirmDialog } from './confirm-dialog';
import { Confirmations } from './confirmations';

/** Shows the `Confirmations` service's current request; rendered once, in the app shell. */
@Component({
  imports: [ConfirmDialog],
  selector: 'app-confirmation-host',
  templateUrl: './confirmation-host.html',
})
export class ConfirmationHost {
  protected readonly confirmations = inject(Confirmations);
  /**
   * The shown request as a list, so `@for` tracks it by identity: a request that replaces another gets
   * a new dialog, with its own initial focus, instead of reusing the open one.
   */
  protected readonly requests = computed(() => {
    const request = this.confirmations.current();
    return request ? [request] : [];
  });

  constructor() {
    // Without the host (signed out), nobody can see the question: never leave it pending.
    inject(DestroyRef).onDestroy(() => this.confirmations.answer(false));
  }
}
