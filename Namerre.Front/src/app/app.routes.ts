import type { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'account',
    loadComponent: () => import('./auth/account/account').then((m) => m.Account),
  },
  {
    path: '',
    loadChildren: () => import('./modules/renamer/renamer.routes').then((m) => m.routes),
  },
];
