import { inject } from '@angular/core';
import { Router, type Routes } from '@angular/router';
import { Auth } from './auth/auth';

export const routes: Routes = [
  {
    path: 'account',
    loadComponent: () => import('./auth/account/account').then((m) => m.Account),
  },
  {
    path: 'users',
    // Administrators only; the server enforces the same rule on every request.
    canActivate: [() => inject(Auth).isAdmin() || inject(Router).parseUrl('/')],
    loadComponent: () => import('./users/users').then((m) => m.Users),
  },
  {
    path: '',
    loadChildren: () => import('./modules/renamer/renamer.routes').then((m) => m.routes),
  },
];
