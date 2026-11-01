import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  type ElementRef,
  inject,
  type OnInit,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom, type Observable } from 'rxjs';
import { Auth, type LinkedLogin, minimumPasswordLength, type UserRole } from '../auth/auth';
import { openModal } from '../workflow-mode/workflow-onboarding';
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
 * own account, last sign-in method); this page shows its explanations.
 */
@Component({
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  selector: 'app-users',
  styleUrl: './users.scss',
  templateUrl: './users.html',
})
export class Users implements OnInit {
  protected readonly auth = inject(Auth);
  private readonly api = inject(UsersApi);
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
  protected readonly confirmingDelete = signal(false);
  protected readonly minimumPasswordLength = minimumPasswordLength;
  protected readonly createError = signal('');
  private readonly createDialog = viewChild<ElementRef<HTMLDialogElement>>('createDialog');
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

  ngOnInit(): void {
    void this.load();
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
    return (user.hasPassword ? 1 : 0) + user.logins.length;
  }

  protected select(user: UserView): void {
    this.selectedId.set(this.selectedId() === user.id ? null : user.id);
    this.confirmingDelete.set(false);
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
    if (!dialog) return;
    if (typeof dialog.close === 'function') dialog.close();
    else dialog.removeAttribute('open');
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
    );
    if (created) {
      this.closeCreate();
      this.createForm.reset({ username: '', role: 'User', password: '', subject: '' });
    } else {
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
    const updated = await this.apply(
      this.api.setPassword(user.id, this.passwordForm.value),
      `${user.hasPassword ? 'Reset' : 'Set'} the password of ${user.username}. Their sessions ended.`,
    );
    if (updated) this.passwordForm.reset();
  }

  protected removePassword(user: UserView): Promise<unknown> {
    return this.apply(
      this.api.removePassword(user.id),
      `Removed the password of ${user.username}.`,
    );
  }

  protected async link(user: UserView): Promise<void> {
    if (this.linkForm.invalid) return;
    const { subject, displayName } = this.linkForm.getRawValue();
    const updated = await this.apply(
      this.api.link(user.id, subject.trim(), displayName.trim() || null),
      `Linked the identity to ${user.username}.`,
    );
    if (updated) this.linkForm.reset();
  }

  protected async move(user: UserView): Promise<void> {
    const { loginId, source } = this.moveForm.getRawValue();
    if (loginId === null) return;
    const owner = this.movable().find((entry) => entry.login.id === Number(loginId))?.owner;
    if (source === 'merge' && !owner) return;
    const updated = await this.apply(
      source === 'merge' && owner
        ? this.api.merge(user.id, owner.id)
        : this.api.moveLogin(user.id, Number(loginId), source === 'delete'),
      source === 'merge'
        ? `Merged ${owner?.username ?? 'the user'} into ${user.username}, with its identities and run history.`
        : `Moved the identity to ${user.username}${source === 'delete' ? ' and deleted the user it left' : ''}.`,
    );
    if (updated) this.moveForm.reset({ loginId: null, source: 'keep' });
  }

  protected unlink(user: UserView, login: LinkedLogin): Promise<unknown> {
    return this.apply(
      this.api.unlink(user.id, login.id),
      `Unlinked ${login.displayName} from ${user.username}.`,
    );
  }

  protected async remove(user: UserView): Promise<void> {
    const removed = await this.apply(this.api.delete(user.id), `Deleted ${user.username}.`);
    if (removed) {
      this.selectedId.set(null);
      this.confirmingDelete.set(false);
    }
  }

  protected async load(): Promise<void> {
    try {
      this.users.set(await firstValueFrom(this.api.list()));
    } catch (error: unknown) {
      this.error.set(this.describe(error));
    }
  }

  /** Sends one change, then reloads every user; returns false when the server refused it. */
  private async apply(request: Observable<unknown>, success: string): Promise<boolean> {
    if (this.busy()) return false;
    this.busy.set(true);
    this.error.set('');
    this.status.set('');
    try {
      await firstValueFrom(request);
      this.status.set(success);
      return true;
    } catch (error: unknown) {
      this.error.set(this.describe(error));
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
