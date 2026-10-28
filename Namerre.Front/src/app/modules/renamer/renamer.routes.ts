import type { Routes } from '@angular/router';
import { RenamerComponent } from './components/renamer/renamer.component';

export const routes: Routes = [
  {
    path: 'runs/:id',
    loadComponent: () =>
      import('./components/run-history/run-history.component').then((m) => m.RunHistoryComponent),
  },
  {
    path: 'runs',
    loadComponent: () =>
      import('./components/run-history/run-history.component').then((m) => m.RunHistoryComponent),
  },
  { path: '', component: RenamerComponent },
];
