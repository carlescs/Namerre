import { HttpClient, HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { computed, DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { catchError, firstValueFrom, throwError } from 'rxjs';
import { defaultLimits, type Limits, WorkloadLimits } from '../workload-limits';
import {
  authenticationJson,
  type CreationOptionsJson,
  creationOptions,
  type RequestOptionsJson,
  registrationJson,
  requestOptions,
} from './passkeys';

/** Matches the server's rule for new passwords; existing shorter passwords still sign in. */
export const minimumPasswordLength = 12;

/** The `message` the server explains a rejected request with, e.g. a too-short password. */
export function serverMessage(error: unknown, status: number): string | null {
  return error instanceof HttpErrorResponse &&
    error.status === status &&
    typeof error.error?.message === 'string'
    ? error.error.message
    : null;
}

/** The server's error `code` for a rejected request, e.g. `LastSignInMethod`. */
export function serverCode(error: unknown): string | null {
  return error instanceof HttpErrorResponse && typeof error.error?.code === 'string'
    ? error.error.code
    : null;
}

export interface SingleSignOnProvider {
  displayName: string;
}

/** What a user may do; managed by administrators in Namerre, never by the identity provider. */
export type UserRole = 'Admin' | 'User';

/** A single sign-on identity linked to a local user. */
export interface LinkedLogin {
  id: number;
  issuer: string;
  displayName: string;
  linkedAt: string;
  lastUsedAt: string | null;
}

/** A passkey registered to the user; the key itself never leaves the server. */
export interface Passkey {
  id: number;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
}

/** A WebAuthn ceremony from the server: options for the browser and the ticket to send back. */
interface PasskeyChallenge<T> {
  options: T;
  ticket: string;
}

interface Session {
  username: string | null;
  userId?: number | null;
  role?: UserRole | null;
  requiresSetup: boolean;
  canChangePassword?: boolean;
  hasPassword?: boolean;
  /** The break-glass administrator from the server configuration. */
  managedByConfiguration?: boolean;
  singleSignOnSession?: boolean;
  /** Signed in within the last few minutes, so sensitive changes need no confirmation. */
  recentSignIn?: boolean;
  logins?: LinkedLogin[] | null;
  /** Whether the server offers passkey sign-in. */
  passkeysEnabled?: boolean;
  passkeys?: Passkey[] | null;
  oidc?: SingleSignOnProvider | null;
  /** Configured workload limits; only sent to signed-in sessions. */
  limits?: Limits | null;
}

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly limits = inject(WorkloadLimits);
  readonly username = signal<string | null>(null);
  readonly requiresSetup = signal(false);
  readonly canChangePassword = signal(false);
  readonly singleSignOn = signal<SingleSignOnProvider | null>(null);
  readonly userId = signal<number | null>(null);
  readonly role = signal<UserRole | null>(null);
  readonly isAdmin = computed(() => this.role() === 'Admin');
  readonly hasPassword = signal(false);
  readonly managedByConfiguration = signal(false);
  readonly singleSignOnSession = signal(false);
  readonly recentSignIn = signal(false);
  readonly logins = signal<LinkedLogin[]>([]);
  readonly passkeysEnabled = signal(false);
  readonly passkeys = signal<Passkey[]>([]);

  async refresh(): Promise<void> {
    const session = await firstValueFrom(this.http.get<Session>('/api/auth/session'));
    this.username.set(session.username);
    this.requiresSetup.set(session.requiresSetup);
    this.canChangePassword.set(session.canChangePassword ?? false);
    this.singleSignOn.set(session.oidc ?? null);
    this.userId.set(session.userId ?? null);
    this.role.set(session.role ?? null);
    this.hasPassword.set(session.hasPassword ?? false);
    this.managedByConfiguration.set(session.managedByConfiguration ?? false);
    this.singleSignOnSession.set(session.singleSignOnSession ?? false);
    this.recentSignIn.set(session.recentSignIn ?? false);
    this.logins.set(session.logins ?? []);
    this.passkeysEnabled.set(session.passkeysEnabled ?? false);
    this.passkeys.set(session.passkeys ?? []);
    this.limits.current.set(session.limits ?? defaultLimits);
  }

  /**
   * Leaves the app for the identity provider, which returns to `/` after sign-in, or to the account page
   * (or the users page) when confirming a sensitive change there.
   */
  startSingleSignOn(returnTo?: 'account' | 'users'): void {
    this.document.location.assign(
      returnTo ? `/api/auth/oidc/login?returnTo=${returnTo}` : '/api/auth/oidc/login',
    );
  }

  /**
   * Starts linking a single sign-on identity to this user. Needs a recent sign-in or the password; the
   * identity provider then returns to the account page.
   */
  async linkSingleSignOn(password?: string): Promise<void> {
    await this.refresh();
    const { url } = await firstValueFrom(
      this.http.post<{ url: string }>('/api/auth/oidc/link', { password: password || null }),
    );
    this.document.location.assign(url);
  }

  async unlink(loginId: number): Promise<void> {
    await this.refresh();
    await firstValueFrom(this.http.delete(`/api/auth/logins/${loginId}`));
    await this.refresh();
  }

  /** Confirms the password, so sensitive changes in the next few minutes need no further prompt. */
  async reauthenticate(password: string): Promise<void> {
    await this.refresh();
    await firstValueFrom(this.http.post('/api/auth/reauthenticate', { password }));
    await this.refresh();
  }

  async removePassword(): Promise<void> {
    await this.refresh();
    await firstValueFrom(this.http.delete('/api/auth/password'));
    await this.refresh();
  }

  async setup(token: string, username: string, password: string): Promise<void> {
    await this.refresh();
    await firstValueFrom(this.http.post('/api/auth/setup', { token, username, password }));
    await this.refresh();
  }

  async login(username: string, password: string): Promise<void> {
    // Refresh the CSRF token in case the previous session expired or another tab signed out.
    await this.refresh();
    await firstValueFrom(this.http.post('/api/auth/login', { username, password }));
    await this.refresh();
  }

  /**
   * Signs in with a passkey: the browser offers the passkeys it holds for Namerre and asks for the
   * device's PIN or biometric. Throws a `DOMException` when the person cancels.
   */
  async loginWithPasskey(): Promise<void> {
    await this.refresh();
    await this.passkeyAssertion('/api/auth/passkey/login');
    await this.refresh();
  }

  /** Confirms it is the user with one of their passkeys, like `reauthenticate` with the password. */
  async reauthenticateWithPasskey(): Promise<void> {
    await this.refresh();
    await this.passkeyAssertion('/api/auth/passkey/reauthenticate');
    await this.refresh();
  }

  /**
   * Registers a new passkey on this device. Needs a recent sign-in or the password; the browser then
   * creates the passkey after the device's PIN or biometric.
   */
  async registerPasskey(name: string, password?: string): Promise<void> {
    await this.refresh();
    const challenge = await firstValueFrom(
      this.http.post<PasskeyChallenge<CreationOptionsJson>>('/api/auth/passkeys/options', {
        password: password || null,
      }),
    );
    const credential = (await this.document.defaultView?.navigator.credentials.create({
      publicKey: creationOptions(challenge.options),
    })) as PublicKeyCredential | null;
    if (!credential) throw new DOMException('No passkey was created.', 'NotAllowedError');
    await firstValueFrom(
      this.http.post('/api/auth/passkeys', {
        ticket: challenge.ticket,
        credential: registrationJson(credential),
        name: name.trim() || null,
      }),
    );
    await this.refresh();
  }

  /** Renames one of the user's passkeys; only the label changes, so no confirmation is needed. */
  async renamePasskey(passkeyId: number, name: string): Promise<void> {
    await this.refresh();
    this.passkeys.set(
      await firstValueFrom(
        this.http.patch<Passkey[]>(`/api/auth/passkeys/${passkeyId}`, { name: name.trim() }),
      ),
    );
  }

  async removePasskey(passkeyId: number): Promise<void> {
    await this.refresh();
    await firstValueFrom(this.http.delete(`/api/auth/passkeys/${passkeyId}`));
    await this.refresh();
  }

  private async passkeyAssertion(url: string): Promise<void> {
    const challenge = await firstValueFrom(
      this.http.post<PasskeyChallenge<RequestOptionsJson>>('/api/auth/passkey/options', {}),
    );
    const credential = (await this.document.defaultView?.navigator.credentials.get({
      publicKey: requestOptions(challenge.options),
    })) as PublicKeyCredential | null;
    if (!credential) throw new DOMException('No passkey was chosen.', 'NotAllowedError');
    await firstValueFrom(
      this.http.post(url, { ticket: challenge.ticket, credential: authenticationJson(credential) }),
    );
  }

  /** Changes the password; users without one yet set it with `currentPassword` null after a recent sign-in. */
  async changePassword(currentPassword: string | null, newPassword: string): Promise<void> {
    await this.refresh();
    await firstValueFrom(this.http.post('/api/auth/password', { currentPassword, newPassword }));
    await this.refresh();
  }

  /** Ends the session; single sign-on sessions then leave for the identity provider's sign-out. */
  async logout(): Promise<void> {
    const result = await firstValueFrom(
      this.http.post<{ endSessionUrl?: string } | null>('/api/auth/logout', {}),
    );
    this.username.set(null);
    this.role.set(null);
    if (result?.endSessionUrl) {
      this.document.location.assign(result.endSessionUrl);
    }
  }
}

export const authenticationInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(Auth);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        request.url.startsWith('/api/')
      ) {
        auth.username.set(null);
      }
      return throwError(() => error);
    }),
  );
};
