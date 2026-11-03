import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  DOCUMENT,
  ElementRef,
  Injector,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Confirmations } from '../../dialogs/confirmations';
import { closeModal, openModal } from '../../dialogs/modal';
import {
  Auth,
  type LinkedLogin,
  minimumPasswordLength,
  type Passkey,
  serverCode,
  serverMessage,
} from '../auth';
import { passkeyCancelled, passkeysSupported } from '../passkeys';

const linkErrors: Record<string, string> = {
  taken: 'That single sign-on identity is already linked to another Namerre user.',
  'access-denied': 'That single sign-on identity is not allowed to use Namerre.',
  expired: 'The link request expired. Start linking again.',
  unavailable: 'The identity provider is unavailable. Try again later.',
};

/** A sensitive change waiting for the user to confirm it is them. */
type Pending = 'link' | 'remove-password' | 'add-passkey';

/**
 * The signed-in user's own account: password (change, set a first one, or remove it), passkeys and
 * the single sign-on identities linked to it, and ending the account's other sessions. A user
 * always keeps at least one way to sign in.
 */
@Component({
  imports: [ReactiveFormsModule, DatePipe],
  selector: 'app-account',
  styleUrl: './account.scss',
  templateUrl: './account.html',
})
export class Account {
  protected readonly auth = inject(Auth);
  private readonly document = inject(DOCUMENT);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly confirmations = inject(Confirmations);
  private readonly confirmDialog = viewChild<ElementRef<HTMLDialogElement>>('confirmDialog');
  private readonly renameDialog = viewChild<ElementRef<HTMLDialogElement>>('renameDialog');
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
  protected readonly passkeyName = new FormControl('', {
    nonNullable: true,
    validators: [Validators.maxLength(100)],
  });
  protected readonly passkeyForm = new FormGroup({ name: this.passkeyName });
  /** The passkey being renamed in the rename dialog, if any, and its new name. */
  protected readonly renaming = signal<Passkey | null>(null);
  protected readonly renameError = signal('');
  protected readonly newPasskeyName = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/\S/), Validators.maxLength(100)],
  });
  protected readonly renameForm = new FormGroup({ name: this.newPasskeyName });
  /** Whether this browser can create and use passkeys here (HTTPS or localhost). */
  protected readonly passkeysSupported = passkeysSupported();
  /** Passkeys can confirm it is the user when they have one and the browser can use it. */
  protected readonly canConfirmWithPasskey = computed(
    () => this.passkeysSupported && this.auth.passkeysEnabled() && this.auth.passkeys().length > 0,
  );
  /** Single sign-on can confirm it is the user when they have a linked identity. */
  protected readonly canConfirmWithSingleSignOn = computed(
    () => this.auth.singleSignOn() !== null && this.auth.logins().length > 0,
  );
  protected readonly minimumPasswordLength = minimumPasswordLength;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly changed = signal(false);
  protected readonly pending = signal<Pending | null>(null);
  protected readonly accessError = signal('');
  protected readonly accessStatus = signal('');
  protected readonly sessionsError = signal('');
  protected readonly sessionsStatus = signal('');
  protected readonly roleLabel = computed(() =>
    this.auth.role() === 'Admin' ? ' (administrator)' : this.auth.role() ? ' (user)' : '',
  );
  /** Setting a first password needs a recent sign-in, since there is no current password to confirm. */
  protected readonly setsFirstPassword = computed(() => !this.auth.hasPassword());
  /** The last way to sign in can never be removed. */
  protected readonly signInMethods = computed(
    () =>
      (this.auth.hasPassword() ? 1 : 0) + this.auth.logins().length + this.auth.passkeys().length,
  );

  constructor() {
    // "Confirm it is you" is a modal dialog, open while a sensitive change waits for it.
    afterRenderEffect(() => {
      const dialog = this.confirmDialog()?.nativeElement;
      if (!dialog) return;
      if (this.pending()) openModal(dialog);
      else if (dialog.hasAttribute('open')) closeModal(dialog);
    });
    afterRenderEffect(() => {
      const dialog = this.renameDialog()?.nativeElement;
      if (!dialog) return;
      if (this.renaming() === null) {
        if (dialog.hasAttribute('open')) closeModal(dialog);
      } else if (!dialog.hasAttribute('open')) {
        openModal(dialog);
        const input = dialog.querySelector('input');
        input?.focus();
        input?.select();
      }
    });
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
          ? 'Confirm it is you first (sign in again or use a passkey), then set the password.'
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

  /** Asks before removing the password, then removes it once it is confirmed to be the user. */
  protected async removePassword(): Promise<void> {
    const confirmed = await this.confirmations.ask({
      heading: 'Remove password?',
      message: `You will sign in with ${
        this.auth.passkeys().length && this.auth.logins().length
          ? 'a passkey or single sign-on'
          : this.auth.passkeys().length
            ? 'a passkey'
            : 'single sign-on'
      } only. Every other browser session is signed out.`,
      confirmLabel: 'Remove password',
      danger: true,
    });
    if (confirmed) this.request('remove-password');
  }

  protected cancel(): void {
    if (this.busy()) return;
    this.pending.set(null);
    this.confirmPassword.reset();
  }

  /** Confirms with the password; users without one use a passkey or single sign-on instead. */
  protected async confirm(): Promise<void> {
    const action = this.pending();
    if (!action || this.confirmPassword.invalid || this.busy()) return;
    if (action === 'link' || action === 'add-passkey') {
      // Linking and adding a passkey check the password themselves, in the request that starts them.
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

  /** Confirms it is the user with one of their passkeys, then runs the pending change. */
  protected async confirmWithPasskey(): Promise<void> {
    const action = this.pending();
    if (this.busy()) return;
    this.busy.set(true);
    this.accessError.set('');
    try {
      await this.auth.reauthenticateWithPasskey();
    } catch (error: unknown) {
      if (!passkeyCancelled(error)) {
        this.accessError.set(this.describe(error, 'Unable to confirm with a passkey.'));
      }
      this.busy.set(false);
      return;
    }
    this.busy.set(false);
    if (action) await this.run(action);
    else this.accessStatus.set('Confirmed. You can make sensitive changes for a few minutes.');
  }

  /** Signs this account out everywhere else, keeping the current browser signed in. */
  protected async signOutOtherSessions(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.sessionsError.set('');
    this.sessionsStatus.set('');
    try {
      await this.auth.signOutOtherSessions();
      this.sessionsStatus.set('Signed out of every other session.');
    } catch (error: unknown) {
      this.sessionsError.set(this.describe(error, 'Unable to sign out of other sessions.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected signInAgain(): void {
    this.auth.startSingleSignOn('account');
  }

  protected startRenaming(passkey: Passkey): void {
    this.newPasskeyName.reset(passkey.name);
    this.renameError.set('');
    this.accessError.set('');
    this.accessStatus.set('');
    this.renaming.set(passkey);
  }

  protected cancelRenaming(): void {
    if (this.busy()) return;
    this.stopRenaming();
  }

  /** Closes the rename dialog and returns focus to the passkey's Rename button. */
  private stopRenaming(): void {
    const passkey = this.renaming();
    this.renaming.set(null);
    if (passkey === null) return;
    afterNextRender(
      () =>
        this.host.nativeElement
          .querySelector<HTMLElement>(`[data-rename-passkey="${passkey.id}"]`)
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected async renamePasskey(passkey: Passkey): Promise<void> {
    if (this.busy() || this.newPasskeyName.invalid) return;
    const name = this.newPasskeyName.value.trim();
    if (name === passkey.name) {
      this.stopRenaming();
      return;
    }
    this.busy.set(true);
    this.renameError.set('');
    try {
      await this.auth.renamePasskey(passkey.id, name);
      this.busy.set(false);
      this.stopRenaming();
      this.accessStatus.set(`Renamed ${passkey.name} to ${name}.`);
    } catch (error: unknown) {
      // Keep the dialog open and explain there, rather than behind it.
      this.renameError.set(this.describe(error, 'Unable to rename the passkey.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removePasskey(passkey: Passkey): Promise<void> {
    if (this.busy()) return;
    const confirmed = await this.confirmations.ask({
      heading: `Remove passkey ${passkey.name}?`,
      message:
        'You will no longer be able to sign in with it, and every other browser session is signed out.',
      confirmLabel: 'Remove passkey',
      danger: true,
    });
    if (!confirmed || this.busy()) return;
    this.busy.set(true);
    this.accessError.set('');
    this.accessStatus.set('');
    try {
      await this.auth.removePasskey(passkey.id);
      this.accessStatus.set(
        `Removed ${passkey.name}. Also delete it from your device or password manager.`,
      );
    } catch (error: unknown) {
      this.accessError.set(this.describe(error, 'Unable to remove the passkey.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async unlink(login: LinkedLogin): Promise<void> {
    if (this.busy()) return;
    const confirmed = await this.confirmations.ask({
      heading: `Unlink ${login.displayName}?`,
      message: 'You will no longer be able to sign in with this single sign-on identity.',
      confirmLabel: 'Unlink',
      danger: true,
    });
    if (!confirmed || this.busy()) return;
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
      if (action === 'add-passkey') {
        await this.auth.registerPasskey(this.passkeyName.value, password);
        this.pending.set(null);
        this.passkeyName.reset();
        this.accessStatus.set('Passkey added. You can now sign in with it.');
        return;
      }
      await this.auth.removePassword();
      this.pending.set(null);
      this.accessStatus.set(
        'Password removed. Sign in with a passkey or single sign-on from now on.',
      );
    } catch (error: unknown) {
      // Closing the browser's passkey prompt is not an error worth reporting.
      if (passkeyCancelled(error)) {
        this.pending.set(null);
        return;
      }
      this.accessError.set(
        this.describe(
          error,
          action === 'link'
            ? 'Unable to start linking.'
            : action === 'add-passkey'
              ? 'Unable to add the passkey.'
              : 'Unable to remove the password.',
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
        : this.canConfirmWithPasskey()
          ? 'Use one of your passkeys to confirm it is you.'
          : 'Sign in again with single sign-on to confirm it is you.';
    }
    if (error instanceof DOMException && error.name === 'InvalidStateError') {
      return 'This device already has a passkey for your account.';
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
