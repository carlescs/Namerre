import { Injectable, signal } from '@angular/core';

/** What a confirmation asks; `message` is the plain-text explanation under the heading. */
export interface ConfirmationRequest {
  heading: string;
  message: string;
  /** Defaults to "Confirm". Name the action, e.g. "Remove passkey". */
  confirmLabel?: string;
  /** Defaults to "Cancel". */
  cancelLabel?: string;
  /** For destructive actions: a red confirm button, and Cancel takes the initial focus. */
  danger?: boolean;
}

interface Pending extends ConfirmationRequest {
  resolve: (confirmed: boolean) => void;
}

/**
 * Asks the user to confirm something in a modal dialog (`ConfirmationHost`, in the app shell) and
 * resolves to whether they did. Asking again while one is open cancels the earlier one.
 */
@Injectable({ providedIn: 'root' })
export class Confirmations {
  private readonly pending = signal<Pending | null>(null);
  /** The confirmation being shown, if any. */
  readonly current = this.pending.asReadonly();

  ask(request: ConfirmationRequest): Promise<boolean> {
    this.pending()?.resolve(false);
    return new Promise((resolve) => this.pending.set({ ...request, resolve }));
  }

  /** Closes the shown confirmation with the user's answer. */
  answer(confirmed: boolean): void {
    const pending = this.pending();
    this.pending.set(null);
    pending?.resolve(confirmed);
  }
}
