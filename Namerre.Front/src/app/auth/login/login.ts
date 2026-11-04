import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Logo } from '../../brand/logo';
import { Auth } from '../auth';
import { passkeyCancelled, passkeysSupported } from '../passkeys';
import { SingleSignOn } from '../single-sign-on/single-sign-on';

@Component({
  imports: [ReactiveFormsModule, SingleSignOn, Logo],
  selector: 'app-login',
  styleUrl: './login.scss',
  templateUrl: './login.html',
})
export class Login {
  private readonly auth = inject(Auth);
  protected readonly form = new FormGroup({
    username: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(128)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(1024)],
    }),
  });
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  /** Passkeys need the server to offer them and a browser that can use them here. */
  protected readonly passkeys = computed(() => this.auth.passkeysEnabled() && passkeysSupported());

  protected async signInWithPasskey(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.loginWithPasskey();
    } catch (error: unknown) {
      if (!passkeyCancelled(error)) {
        this.error.set(
          error instanceof HttpErrorResponse && error.status === 401
            ? 'That passkey was not accepted. Use a passkey registered to your Namerre account.'
            : error instanceof HttpErrorResponse && error.status === 429
              ? 'Too many sign-in attempts. Wait one minute and try again.'
              : 'Unable to sign in with a passkey. Check your connection and use HTTPS, then try again.',
        );
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const { username, password } = this.form.getRawValue();
    try {
      await this.auth.login(username, password);
    } catch (error: unknown) {
      this.error.set(passwordSignInError(error));
    } finally {
      this.form.controls.password.reset();
      this.busy.set(false);
    }
  }
}

/** The message for a failed password sign-in. */
function passwordSignInError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Unable to sign in. Check your connection and use HTTPS, then try again.';
  }
  const code = (error.error as { code?: unknown } | null)?.code;
  if (error.status === 401) return 'Invalid username or password.';
  if (error.status === 403 && code === 'PasskeyRequired') {
    return 'Administrators sign in with a passkey or single sign-on.';
  }
  if (error.status === 429) {
    return code === 'SignInThrottled'
      ? 'Too many failed sign-ins for this account. Wait a minute and try again.'
      : 'Too many sign-in attempts. Wait one minute and try again.';
  }
  return 'Unable to sign in. Check your connection and use HTTPS, then try again.';
}
