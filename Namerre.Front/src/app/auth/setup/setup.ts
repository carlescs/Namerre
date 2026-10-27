import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Logo } from '../../brand/logo';
import { Auth, minimumPasswordLength, serverMessage } from '../auth';
import { SingleSignOn } from '../single-sign-on/single-sign-on';

@Component({
  imports: [ReactiveFormsModule, SingleSignOn, Logo],
  selector: 'app-setup',
  styleUrl: '../login/login.scss',
  templateUrl: './setup.html',
})
export class Setup {
  private readonly auth = inject(Auth);
  protected readonly form = new FormGroup(
    {
      token: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.minLength(64), Validators.maxLength(64)],
      }),
      username: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(/\S/), Validators.maxLength(128)],
      }),
      password: new FormControl('', {
        nonNullable: true,
        validators: [
          Validators.required,
          Validators.minLength(minimumPasswordLength),
          Validators.maxLength(1024),
        ],
      }),
      confirmation: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    {
      validators: (form) =>
        form.get('password')?.value === form.get('confirmation')?.value
          ? null
          : { passwordMismatch: true },
    },
  );
  protected readonly minimumPasswordLength = minimumPasswordLength;
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const { token, username, password } = this.form.getRawValue();
    try {
      await this.auth.setup(token, username, password);
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.error.set('The administrator is already configured. Reload the page to sign in.');
      } else {
        this.error.set(
          error instanceof HttpErrorResponse && error.status === 403
            ? 'Invalid setup token. Copy the token from the current server console.'
            : error instanceof HttpErrorResponse && error.status === 429
              ? 'Too many attempts. Wait one minute and try again.'
              : error instanceof HttpErrorResponse && error.status === 503
                ? 'Unable to save the administrator. Check the server database and permissions, then retry.'
                : (serverMessage(error, 400) ??
                  'Unable to complete installation. Check your connection and use HTTPS, then retry.'),
        );
      }
    } finally {
      this.form.controls.password.reset();
      this.form.controls.confirmation.reset();
      this.busy.set(false);
    }
  }
}
