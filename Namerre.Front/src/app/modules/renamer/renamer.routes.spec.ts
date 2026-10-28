import type { Type } from '@angular/core';
import type { Route } from '@angular/router';
import { routes as appRoutes } from '../../app.routes';
import { RenamerComponent } from './components/renamer/renamer.component';
import { RunHistoryComponent } from './components/run-history/run-history.component';
import { routes } from './renamer.routes';

describe('renamer routes', () => {
  it('serves the workspace eagerly at the root', () => {
    expect(routes.find((route) => route.path === '')?.component).toBe(RenamerComponent);
  });

  it.each(['runs', 'runs/:id'])('lazy-loads run history for %s', async (path) => {
    const route = routes.find((candidate) => candidate.path === path) as Route;
    const loaded = await (route.loadComponent as () => Promise<Type<unknown>>)();
    expect(loaded).toBe(RunHistoryComponent);
  });

  it('lazy-loads the renamer module from the application root', async () => {
    const root = appRoutes.find((route) => route.path === '') as Route;
    const children = await (root.loadChildren as () => Promise<Route[]>)();
    expect(children).toBe(routes);
  });
});
