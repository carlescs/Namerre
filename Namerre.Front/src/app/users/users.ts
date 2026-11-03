import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  type ElementRef,
  effect,
  inject,
  type OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom, type Observable } from 'rxjs';
import {
  Auth,
  type LinkedLogin,
  minimumPasswordLength,
  type Passkey,
  serverCode,
  type UserRole,
} from '../auth/auth';
import { ConfirmIdentity } from '../auth/confirm-identity/confirm-identity';
import { Confirmations } from '../dialogs/confirmations';
import { closeModal, openModal } from '../dialogs/modal';
import { type MoveSource, UsersApi, type UserView } from './users-api';

/** An identity linked to some user, offered to move to another one. */
interface LinkedElsewhere {
  login: LinkedLogin;
  owner: UserView;
}

const optionalPassword = [Validators.minLength(minimumPasswordLength), Validators.maxLength(1024)];

/**
 * Administrator page for local users: create, change role, disable, reset or remove passwords, link and
 * unlink single sign-on identities, and delete. The server enforces every rule (last administrator,
 * own account, last sign-in method); this page shows its explanations. Every change needs a recent sign-in:
 * when the server asks for one, the administrator confirms it is them and the change is sent again.
 */
@Component({
  imports: [ConfirmIdentity, DatePipe, ReactiveFormsModule, RouterLink],
  selector: 'app-users',
  styleUrl: './users.scss',
  templateUrl: './users.html',
})
export class Users implements OnInit {
  protected readonly auth = inject(Auth);
  private readonly api = inject(UsersApi);
  private readonly router = inject(Router);
  private readonly confirmations = inject(Confirmations);
  protected readonly users = signal<UserView[] | null>(null);
  protected readonly selectedId = signal<number | null>(null);
  protected readonly selected = computed(
    () => this.users()?.find((user) => user.id === this.selectedId()) ?? null,
  );
  /** Identities linked to other users, for moving one to the selected user. */
  protected readonly movable = computed<LinkedElsewhere[]>(() =>
    (this.users() ?? [])
      .filter((user) => user.id !== this.selectedId())
      .flatMap((owner) => owner.logins.map((login) => ({ login, owner }))),
  );
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly status = signal('');
  protected readonly minimumPasswordLength = minimumPasswordLength;
  protected readonly createError = signal('');
  private readonly createDialog = viewChild<ElementRef<HTMLDialogElement>>('createDialog');
  private readonly confirmDialog = viewChild<ElementRef<HTMLDialogElement>>('confirmDialog');
  /** Sends the change the server refused for lack of a recent sign-in again, once the user confirmed. */
  private retry: (() => Promise<unknown>) | null = null;
  protected readonly confirming = signal(false);
  protected readonly createForm = new FormGroup({
    username: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/\S/), Validators.maxLength(128)],
    }),
    role: new FormControl<UserRole>('User', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true, validators: optionalPassword }),
    subject: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(256)] }),
  });
  protected readonly passwordForm = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/\S/), ...optionalPassword],
  });
  protected readonly passwordGroup = new FormGroup({ password: this.passwordForm });
  protected readonly linkForm = new FormGroup({
    subject: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/\S/), Validators.maxLength(256)],
    }),
    displayName: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(256)],
    }),
  });
  protected readonly moveForm = new FormGroup({
    loginId: new FormControl<number | null>(null, { validators: [Validators.required] }),
    source: new FormControl<MoveSource>('keep', { nonNullable: true }),
  });

  constructor() {
    // The route guard only runs on navigation: when the session changes under this page (an
    // administrator signs out and someone else signs in on the same tab), send non-administrators
    // back to the workspace.
    effect(() => {
      if (this.auth.role() !== null && !this.auth.isAdmin()) void this.router.navigateByUrl('/');
    });
  }

  ngOnInit(): void {
    if (this.auth.isAdmin()) void this.load();
  }

  protected isSelf(user: UserView): boolean {
    return user.id === this.auth.userId();
  }

  /** Labels for the password form: resetting an existing password or setting a first one. */
  protected passwordLabels(user: UserView): { field: string; action: string } {
    return user.hasPassword
      ? { field: 'New password', action: 'Reset password' }
      : { field: 'Set a password', action: 'Set password' };
  }

  protected methods(user: UserView): number {
    return (user.hasPassword ? 1 : 0) + user.logins.length + user.passkeys.length;
  }

  protected select(user: UserView): void {
    this.selectedId.set(this.selectedId() === user.id ? null : user.id);
    this.passwordForm.reset();
    this.linkForm.reset();
    this.moveForm.reset({ loginId: null, source: 'keep' });
    this.error.set('');
    this.status.set('');
  }

  /** Sign-in methods on one line, e.g. "Password, alice". */
  protected methodSummary(user: UserView): string {
    const methods = [
      ...(user.hasPassword ? ['Password'] : []),
      ...user.logins.map((l) => l.displayName),
      ...(user.passkeys.length === 1
        ? ['1 passkey']
        : user.passkeys.length
          ? [`${user.passkeys.length} passkeys`]
          : []),
    ];
    return methods.length ? methods.join(', ') : 'None';
  }

  protected openCreate(): void {
    this.createForm.reset({ username: '', role: 'User', password: '', subject: '' });
    this.createError.set('');
    const dialog = this.createDialog()?.nativeElement;
    if (dialog) openModal(dialog);
  }

  protected closeCreate(): void {
    const dialog = this.createDialog()?.nativeElement;
    if (dialog) closeModal(dialog);
  }

  protected async create(): Promise<void> {
    const { username, role, password, subject } = this.createForm.getRawValue();
    if (this.createForm.invalid || (!password && !subject.trim())) return;
    const created = await this.apply(
      this.api.create({
        username: username.trim(),
        role,
        password: password || null,
        subject: subject.trim() || null,
        displayName: null,
      }),
      `Created ${username.trim()}.`,
      () => {
        this.closeCreate();
        this.createForm.reset({ username: '', role: 'User', password: '', subject: '' });
      },
    );
    if (!created) {
      // Keep the dialog open and explain there, rather than behind it.
      this.createError.set(this.error());
      this.error.set('');
    }
  }

  protected setRole(user: UserView, role: string): Promise<unknown> {
    return this.apply(
      this.api.update(user.id, { role: role as UserRole }),
      `${user.username} is now ${role === 'Admin' ? 'an administrator' : 'a user'}.`,
    );
  }

  protected setDisabled(user: UserView, disabled: boolean): Promise<unknown> {
    return this.apply(
      this.api.update(user.id, { disabled }),
      `${user.username} is ${disabled ? 'disabled and signed out' : 'enabled'}.`,
    );
  }

  protected async resetPassword(user: UserView): Promise<void> {
    if (this.passwordForm.invalid) return;
    await this.apply(
      this.api.setPassword(user.id, this.passwordForm.value),
      `${user.hasPassword ? 'Reset' : 'Set'} the password of ${user.username}. Their sessions ended.`,
      () => this.passwordForm.reset(),
    );
  }

  protected async removePassword(user: UserView): Promise<unknown> {
    const confirmed = await this.confirmations.ask({
      heading: 'Remove password?',
      message: `${user.username} will no longer be able to sign in with a password.`,
      confirmLabel: 'Remove password',
      danger: true,
    });
    if (!confirmed) return false;
    return this.apply(
      this.api.removePassword(user.id),
      `Removed the password of ${user.username}.`,
    );
  }

  protected async link(user: UserView): Promise<void> {
    if (this.linkForm.invalid) return;
    const { subject, displayName } = this.linkForm.getRawValue();
    await this.apply(
      this.api.link(user.id, subject.trim(), displayName.trim() || null),
      `Linked the identity to ${user.username}.`,
      () => this.linkForm.reset(),
    );
  }

  protected async move(user: UserView): Promise<void> {
    const { loginId, source } = this.moveForm.getRawValue();
    if (loginId === null) return;
    const owner = this.movable().find((entry) => entry.login.id === Number(loginId))?.owner;
    if (source === 'merge' && !owner) return;
    if (source !== 'keep') {
      const confirmed = await this.confirmations.ask(
        source === 'merge'
          ? {
              heading: `Merge ${owner?.username ?? 'the user'}?`,
              message: `Its identities and run history move to ${user.username}, then ${owner?.username ?? 'it'} is deleted.`,
              confirmLabel: 'Merge user',
              danger: true,
            }
          : {
              heading: `Delete ${owner?.username ?? 'the user'}?`,
              message: `The identity moves to ${user.username}, then the user it leaves is deleted. Its runs keep its name.`,
              confirmLabel: 'Move and delete',
              danger: true,
            },
      );
      if (!confirmed) return;
    }
    await this.apply(
      source === 'merge' && owner
        ? this.api.merge(user.id, owner.id)
        : this.api.moveLogin(user.id, Number(loginId), source === 'delete'),
      source === 'merge'
        ? `Merged ${owner?.username ?? 'the user'} into ${user.username}, with its identities and run history.`
        : `Moved the identity to ${user.username}${source === 'delete' ? ' and deleted the user it left' : ''}.`,
      () => this.moveForm.reset({ loginId: null, source: 'keep' }),
    );
  }

  protected async unlink(user: UserView, login: LinkedLogin): Promise<unknown> {
    const confirmed = await this.confirmations.ask({
      heading: `Unlink ${login.displayName}?`,
      message: `${user.username} will no longer be able to sign in with this identity.`,
      confirmLabel: 'Unlink',
      danger: true,
    });
    if (!confirmed) return false;
    return this.apply(
      this.api.unlink(user.id, login.id),
      `Unlinked ${login.displayName} from ${user.username}.`,
    );
  }

  protected async removePasskey(user: UserView, passkey: Passkey): Promise<unknown> {
    const confirmed = await this.confirmations.ask({
      heading: `Remove passkey ${passkey.name}?`,
      message: `${user.username} will no longer be able to sign in with it.`,
      confirmLabel: 'Remove passkey',
      danger: true,
    });
    if (!confirmed) return false;
    return this.apply(
      this.api.removePasskey(user.id, passkey.id),
      `Removed passkey ${passkey.name} from ${user.username}.`,
    );
  }

  protected async remove(user: UserView): Promise<void> {
    const confirmed = await this.confirmations.ask({
      heading: `Delete ${user.username}?`,
      message: 'Their runs stay in the history under their name.',
      confirmLabel: 'Delete user',
      danger: true,
    });
    if (!confirmed) return;
    await this.apply(this.api.delete(user.id), `Deleted ${user.username}.`, () =>
      this.selectedId.set(null),
    );
  }

  protected async load(): Promise<void> {
    try {
      this.users.set(await firstValueFrom(this.api.list()));
    } catch (error: unknown) {
      this.error.set(this.describe(error));
    }
  }

  /** The user confirmed it is them: sends the refused change again. */
  protected async confirmed(): Promise<void> {
    this.closeConfirm();
    const retry = this.retry;
    this.retry = null;
    if (retry) await retry();
  }

  protected cancelConfirm(): void {
    this.retry = null;
    this.closeConfirm();
    this.createError.set('');
  }

  private closeConfirm(): void {
    this.confirming.set(false);
    const dialog = this.confirmDialog()?.nativeElement;
    if (dialog) closeModal(dialog);
  }

  /**
   * Sends one change, then reloads every user; returns false when the server refused it. `done` runs after
   * it succeeded, also when it was sent again after confirming. A change that needs a recent sign-in asks
   * the user to confirm first instead of failing.
   */
  private async apply(
    request: Observable<unknown>,
    success: string,
    done?: () => void,
  ): Promise<boolean> {
    if (this.busy()) return false;
    this.busy.set(true);
    this.error.set('');
    this.status.set('');
    try {
      await firstValueFrom(request);
      this.status.set(success);
      done?.();
      return true;
    } catch (error: unknown) {
      if (serverCode(error) === 'RecentSignInRequired') {
        // Observables from the HTTP client are cold, so sending the same one again repeats the request.
        this.retry = () => this.apply(request, success, done);
        this.confirming.set(true);
        const dialog = this.confirmDialog()?.nativeElement;
        if (dialog) openModal(dialog);
      } else {
        this.error.set(this.describe(error));
      }
      return false;
    } finally {
      await this.load();
      this.busy.set(false);
    }
  }

  private describe(error: unknown): string {
    if (error instanceof HttpErrorResponse && typeof error.error?.message === 'string') {
      return error.error.message;
    }
    if (error instanceof HttpErrorResponse && error.status === 403) {
      return 'Only administrators can manage users.';
    }
    return 'Unable to reach Namerre. Check your connection and use HTTPS, then retry.';
  }
}
