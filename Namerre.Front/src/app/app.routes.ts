import { inject } from '@angular/core';
import { Router, type Routes, type UrlTree } from '@angular/router';
import { Auth } from './auth/auth';

/** Lets administrators in; on a cold load it reads the session first, so a refresh of `/users` works. */
async function adminOnly(): Promise<boolean | UrlTree> {
  const auth = inject(Auth);
  const router = inject(Router);
  if (auth.role() === null) {
    try {
      await auth.refresh();
    } catch {
      // Not signed in or offline: fall through to the redirect; the app shell reports the error.
    }
  }
  return auth.isAdmin() || router.parseUrl('/');
}

export const routes: Routes = [
  {
    path: 'account',
    loadComponent: () => import('./auth/account/account').then((m) => m.Account),
  },
  {
    path: 'users',
    // Administrators only; the server enforces the same rule on every request.
    canActivate: [adminOnly],
    loadComponent: () => import('./users/users').then((m) => m.Users),
  },
  {
    path: '',
    loadChildren: () => import('./modules/renamer/renamer.routes').then((m) => m.routes),
  },
];
