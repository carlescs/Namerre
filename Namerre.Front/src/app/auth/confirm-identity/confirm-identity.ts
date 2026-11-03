import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Auth, serverCode } from '../auth';
import { passkeyCancelled, passkeysSupported } from '../passkeys';

/**
 * Asks the signed-in user to prove it is them again (password, passkey or single sign-on), which makes
 * the session a recent sign-in for the next few minutes. Emits `confirmed` once the server accepted the
 * proof; single sign-on leaves the app and returns to the user management page.
 */
@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-confirm-identity',
  templateUrl: './confirm-identity.html',
})
export class ConfirmIdentity {
  protected readonly auth = inject(Auth);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
  protected readonly password = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(1024)],
  });
  protected readonly form = new FormGroup({ password: this.password });
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly canUsePasskey = computed(
    () => passkeysSupported() && this.auth.passkeysEnabled() && this.auth.passkeys().length > 0,
  );
  protected readonly canUseSingleSignOn = computed(
    () => this.auth.singleSignOn() !== null && this.auth.logins().length > 0,
  );

  protected async confirmWithPassword(): Promise<void> {
    if (this.password.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.reauthenticate(this.password.value);
    } catch (error: unknown) {
      this.error.set(this.describe(error, 'Unable to confirm your password.'));
      return;
    } finally {
      this.password.reset();
      this.busy.set(false);
    }
    this.confirmed.emit();
  }

  protected async confirmWithPasskey(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.reauthenticateWithPasskey();
    } catch (error: unknown) {
      // Closing the browser's passkey prompt is not an error worth reporting.
      if (!passkeyCancelled(error)) this.error.set(this.describe(error, 'Unable to confirm.'));
      return;
    } finally {
      this.busy.set(false);
    }
    this.confirmed.emit();
  }

  protected signInAgain(): void {
    this.auth.startSingleSignOn('users');
  }

  private describe(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.status === 429) {
      return 'Too many attempts. Wait one minute and try again.';
    }
    if (error instanceof HttpErrorResponse && error.status === 403) {
      return serverCode(error) === 'InvalidPassword' && typeof error.error?.message === 'string'
        ? error.error.message
        : fallback;
    }
    return `${fallback} Check your connection and use HTTPS, then retry.`;
  }
}
