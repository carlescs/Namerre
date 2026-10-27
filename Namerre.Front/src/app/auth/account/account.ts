import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Auth, minimumPasswordLength, serverMessage } from '../auth';

@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-account',
  styleUrl: './account.scss',
  templateUrl: './account.html',
})
export class Account {
  protected readonly auth = inject(Auth);
  protected readonly form = new FormGroup(
    {
      currentPassword: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.maxLength(1024)],
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
  protected readonly minimumPasswordLength = minimumPasswordLength;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly changed = signal(false);

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.changed.set(false);
    const { currentPassword, newPassword } = this.form.getRawValue();
    try {
      await this.auth.changePassword(currentPassword, newPassword);
      this.changed.set(true);
    } catch (error: unknown) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 403
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
}
