import { Component, inject } from '@angular/core';
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
}
