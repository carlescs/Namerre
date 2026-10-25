import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Auth, authenticationInterceptor } from './auth';

describe('Auth', () => {
  let auth: Auth;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authenticationInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    auth = TestBed.inject(Auth);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.cookie = 'XSRF-TOKEN=; Max-Age=0; Path=/';
  });

  it('loads the session without storing a bearer token', async () => {
    const loading = auth.refresh();
    http.expectOne('/api/auth/session').flush({ username: 'admin', requiresSetup: false });
    await loading;
    expect(auth.username()).toBe('admin');
    expect(auth.singleSignOn()).toBeNull();
  });

  it('reports the configured single sign-on provider', async () => {
    const loading = auth.refresh();
    http.expectOne('/api/auth/session').flush({
      username: null,
      requiresSetup: false,
      oidc: { displayName: 'Keycloak' },
    });
    await loading;
    expect(auth.singleSignOn()).toEqual({ displayName: 'Keycloak' });
  });

  it('refreshes CSRF before login and reloads the authenticated session afterwards', async () => {
    const login = auth.login('admin', 'password');
    http.expectOne('/api/auth/session').flush({ username: null, requiresSetup: false });
    document.cookie = 'XSRF-TOKEN=test-csrf; Path=/';
    const request = await vi.waitFor(() => http.expectOne('/api/auth/login'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ username: 'admin', password: 'password' });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('test-csrf');
    request.flush(null);
    const session = await vi.waitFor(() => http.expectOne('/api/auth/session'));
    session.flush({ username: 'admin', requiresSetup: false });
    await login;
    expect(auth.username()).toBe('admin');
  });

  it('creates the administrator with CSRF protection then reloads setup status', async () => {
    const setup = auth.setup('setup-token', 'admin', 'a-long-unique-password');
    http.expectOne('/api/auth/session').flush({ username: null, requiresSetup: true });
    document.cookie = 'XSRF-TOKEN=setup-csrf; Path=/';
    const request = await vi.waitFor(() => http.expectOne('/api/auth/setup'));
    expect(auth.requiresSetup()).toBe(true);
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('setup-csrf');
    expect(request.request.body).toEqual({
      token: 'setup-token',
      username: 'admin',
      password: 'a-long-unique-password',
    });
    request.flush(null);
    const session = await vi.waitFor(() => http.expectOne('/api/auth/session'));
    session.flush({ username: null, requiresSetup: false });
    await setup;
    expect(auth.requiresSetup()).toBe(false);
    expect(auth.username()).toBeNull();
  });

  it('clears an expired session on an unauthorized response', async () => {
    auth.username.set('admin');
    const logout = auth.logout();
    const failure = expect(logout).rejects.toMatchObject({ status: 401 });
    http.expectOne('/api/auth/logout').flush({}, { status: 401, statusText: 'Unauthorized' });
    await failure;
    expect(auth.username()).toBeNull();
  });

  it('keeps the session visible if sign-out fails', async () => {
    auth.username.set('admin');
    const logout = auth.logout();
    const failure = expect(logout).rejects.toMatchObject({ status: 500 });
    http.expectOne('/api/auth/logout').flush({}, { status: 500, statusText: 'Server error' });
    await failure;
    expect(auth.username()).toBe('admin');
  });
});
