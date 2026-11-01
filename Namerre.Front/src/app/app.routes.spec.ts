import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, type UrlTree } from '@angular/router';
import { routes } from './app.routes';
import { Auth } from './auth/auth';

describe('routes', () => {
  const refresh = vi.fn<() => Promise<void>>();
  let auth: Auth;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    auth = TestBed.inject(Auth);
    refresh.mockReset();
    auth.refresh = refresh;
  });

  function guard(): Promise<boolean | UrlTree> {
    const users = routes.find((route) => route.path === 'users')!;
    const canActivate = users.canActivate![0] as () => Promise<boolean | UrlTree>;
    return TestBed.runInInjectionContext(canActivate);
  }

  it('reads the session first on a cold load of the users page', async () => {
    refresh.mockImplementation(async () => auth.role.set('Admin'));
    expect(await guard()).toBe(true);
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('sends users and signed-out visitors back to the workspace', async () => {
    auth.role.set('User');
    const router = TestBed.inject(Router);
    expect(router.serializeUrl((await guard()) as UrlTree)).toBe('/');
    expect(refresh).not.toHaveBeenCalled();

    auth.role.set(null);
    refresh.mockRejectedValue(new Error('offline'));
    expect(router.serializeUrl((await guard()) as UrlTree)).toBe('/');
  });
});
