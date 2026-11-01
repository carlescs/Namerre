import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DOCUMENT, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Auth, type LinkedLogin, minimumPasswordLength, serverCode, serverMessage } from '../auth';

const linkErrors: Record<string, string> = {
  taken: 'That single sign-on identity is already linked to another Namerre user.',
  'access-denied': 'That single sign-on identity is not allowed to use Namerre.',
  expired: 'The link request expired. Start linking again.',
  unavailable: 'The identity provider is unavailable. Try again later.',
};

/** A sensitive change waiting for the user to confirm it is them. */
type Pending = 'link' | 'remove-password';

/**
 * The signed-in user's own account: password (change, set a first one, or remove it) and the single
 * sign-on identities linked to it. A user always keeps at least one way to sign in.
 */
@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-account',
  styleUrl: './account.scss',
  templateUrl: './account.html',
})
export class Account {
  protected readonly auth = inject(Auth);
  private readonly document = inject(DOCUMENT);
  protected readonly form = new FormGroup(
    {
      currentPassword: new FormControl('', {
        nonNullable: true,
        validators: [Validators.maxLength(1024)],
      }),
      newPassword: new FormControl('', {
        nonNullable: true,
        validators: [
          Validators.required,
          Validators.pattern(/\S/),
          Validators.minLength(minimumPasswordLength),
          Validators.maxLength(1024),
        ],
      }),
      confirmation: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    {
      validators: (form) =>
        form.get('newPassword')?.value === form.get('confirmation')?.value
          ? null
          : { passwordMismatch: true },
    },
  );
  protected readonly confirmPassword = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(1024)],
  });
  protected readonly confirmForm = new FormGroup({ password: this.confirmPassword });
  protected readonly minimumPasswordLength = minimumPasswordLength;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly changed = signal(false);
  protected readonly pending = signal<Pending | null>(null);
  protected readonly accessError = signal('');
  protected readonly accessStatus = signal('');
  protected readonly roleLabel = computed(() =>
    this.auth.role() === 'Admin' ? ' (administrator)' : this.auth.role() ? ' (user)' : '',
  );
  /** Setting a first password needs a recent sign-in, since there is no current password to confirm. */
  protected readonly setsFirstPassword = computed(() => !this.auth.hasPassword());
  /** The last way to sign in can never be removed. */
  protected readonly signInMethods = computed(
    () => (this.auth.hasPassword() ? 1 : 0) + this.auth.logins().length,
  );

  constructor() {
    // The server returns from linking single sign-on through query parameters on `/account`.
    const url = new URL(this.document.location.href);
    const linked = url.searchParams.get('linked');
    const linkError = url.searchParams.get('linkError');
    if (linked !== null)
      this.accessStatus.set('Single sign-on linked. You can now sign in with it.');
    if (linkError !== null) {
      this.accessError.set(linkErrors[linkError] ?? 'Linking single sign-on failed. Try again.');
    }
    if (linked !== null || linkError !== null) {
      url.searchParams.delete('linked');
      url.searchParams.delete('linkError');
      this.document.defaultView?.history.replaceState(
        null,
        '',
        url.pathname + url.search + url.hash,
      );
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) return;
    const firstPassword = this.setsFirstPassword();
    const { currentPassword, newPassword } = this.form.getRawValue();
    if (!firstPassword && !currentPassword) return;
    this.busy.set(true);
    this.error.set('');
    this.changed.set(false);
    try {
      await this.auth.changePassword(firstPassword ? null : currentPassword, newPassword);
      this.changed.set(true);
    } catch (error: unknown) {
      this.error.set(
        serverCode(error) === 'RecentSignInRequired'
          ? 'Sign in again with single sign-on to confirm it is you, then set the password.'
          : error instanceof HttpErrorResponse && error.status === 403
            ? 'The current password is incorrect.'
            : error instanceof HttpErrorResponse && error.status === 409
              ? (error.error?.message ?? 'The password cannot be changed here.')
              : error instanceof HttpErrorResponse && error.status === 429
                ? 'Too many attempts. Wait one minute and try again.'
                : error instanceof HttpErrorResponse && error.status === 503
                  ? 'Unable to save the password. Check the server database and permissions, then retry.'
                  : (serverMessage(error, 400) ??
                    'Unable to change the password. Check your connection and use HTTPS, then retry.'),
      );
    } finally {
      this.form.reset();
      this.busy.set(false);
    }
  }

  /** Runs a sensitive change now when the sign-in is recent, otherwise asks the user to confirm first. */
  protected request(action: Pending): void {
    this.accessError.set('');
    this.accessStatus.set('');
    if (this.auth.recentSignIn()) {
      void this.run(action);
    } else {
      this.confirmPassword.reset();
      this.pending.set(action);
    }
  }

  protected cancel(): void {
    this.pending.set(null);
    this.confirmPassword.reset();
  }

  /** Confirms with the password; users without one sign in again with single sign-on instead. */
  protected async confirm(): Promise<void> {
    const action = this.pending();
    if (!action || this.confirmPassword.invalid || this.busy()) return;
    if (action === 'link') {
      // Linking checks the password itself, in the same request that starts it.
      await this.run(action, this.confirmPassword.value);
      return;
    }
    this.busy.set(true);
    try {
      await this.auth.reauthenticate(this.confirmPassword.value);
    } catch (error: unknown) {
      this.accessError.set(this.describe(error, 'Unable to confirm your password.'));
      this.busy.set(false);
      return;
    } finally {
      this.confirmPassword.reset();
    }
    this.busy.set(false);
    await this.run(action);
  }

  protected signInAgain(): void {
    this.auth.startSingleSignOn('account');
  }

  protected async unlink(login: LinkedLogin): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.accessError.set('');
    this.accessStatus.set('');
    try {
      await this.auth.unlink(login.id);
      this.accessStatus.set(`Unlinked ${login.displayName}.`);
    } catch (error: unknown) {
      this.accessError.set(this.describe(error, 'Unable to unlink the identity.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(action: Pending, password?: string): Promise<void> {
    this.busy.set(true);
    this.accessError.set('');
    try {
      if (action === 'link') {
        // Leaves the app for the identity provider, which returns to this page.
        await this.auth.linkSingleSignOn(password);
        return;
      }
      await this.auth.removePassword();
      this.pending.set(null);
      this.accessStatus.set('Password removed. Sign in with single sign-on from now on.');
    } catch (error: unknown) {
      this.accessError.set(
        this.describe(
          error,
          action === 'link' ? 'Unable to start linking.' : 'Unable to remove the password.',
        ),
      );
      if (serverCode(error) === 'RecentSignInRequired') this.pending.set(action);
    } finally {
      this.confirmPassword.reset();
      this.busy.set(false);
    }
  }

  private describe(error: unknown, fallback: string): string {
    if (serverCode(error) === 'RecentSignInRequired') {
      return this.auth.hasPassword()
        ? 'Enter your password to confirm it is you.'
        : 'Sign in again with single sign-on to confirm it is you.';
    }
    if (error instanceof HttpErrorResponse && error.status === 429) {
      return 'Too many attempts. Wait one minute and try again.';
    }
    if (error instanceof HttpErrorResponse && error.status === 403) {
      return typeof error.error?.message === 'string' ? error.error.message : fallback;
    }
    return error instanceof HttpErrorResponse && typeof error.error?.message === 'string'
      ? error.error.message
      : `${fallback} Check your connection and use HTTPS, then retry.`;
  }
}
