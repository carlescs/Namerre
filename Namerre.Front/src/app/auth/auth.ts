import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, firstValueFrom, throwError } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  readonly username = signal<string | null>(null);
  readonly requiresSetup = signal(false);

  async refresh(): Promise<void> {
    const session = await firstValueFrom(
      this.http.get<{ username: string | null; requiresSetup: boolean }>('/api/auth/session'),
    );
    this.username.set(session.username);
    this.requiresSetup.set(session.requiresSetup);
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
