import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { DOCUMENT, inject, Injectable, signal } from '@angular/core';
import { catchError, firstValueFrom, throwError } from 'rxjs';

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

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/logout', {}));
    this.username.set(null);
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
