import { HttpClient, HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { catchError, firstValueFrom, throwError } from 'rxjs';

/** Matches the server's rule for new administrator passwords; existing shorter passwords still sign in. */
export const minimumPasswordLength = 12;

/** The `message` the server explains a rejected request with, e.g. a too-short password. */
export function serverMessage(error: unknown, status: number): string | null {
  return error instanceof HttpErrorResponse &&
    error.status === status &&
    typeof error.error?.message === 'string'
    ? error.error.message
    : null;
}

export interface SingleSignOnProvider {
  displayName: string;
}

interface Session {
  username: string | null;
  requiresSetup: boolean;
  canChangePassword?: boolean;
  oidc?: SingleSignOnProvider | null;
}

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  readonly username = signal<string | null>(null);
  readonly requiresSetup = signal(false);
  readonly canChangePassword = signal(false);
  readonly singleSignOn = signal<SingleSignOnProvider | null>(null);

  async refresh(): Promise<void> {
    const session = await firstValueFrom(this.http.get<Session>('/api/auth/session'));
    this.username.set(session.username);
    this.requiresSetup.set(session.requiresSetup);
    this.canChangePassword.set(session.canChangePassword ?? false);
    this.singleSignOn.set(session.oidc ?? null);
  }

  /** Leaves the app for the identity provider, which returns to `/` after sign-in. */
  startSingleSignOn(): void {
    this.document.location.assign('/api/auth/oidc/login');
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

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await this.refresh();
    await firstValueFrom(this.http.post('/api/auth/password', { currentPassword, newPassword }));
  }

  /** Ends the session; single sign-on sessions then leave for the identity provider's sign-out. */
  async logout(): Promise<void> {
    const result = await firstValueFrom(
      this.http.post<{ endSessionUrl?: string } | null>('/api/auth/logout', {}),
    );
    this.username.set(null);
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
