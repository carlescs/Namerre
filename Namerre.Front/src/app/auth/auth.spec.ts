import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { defaultLimits, WorkloadLimits } from '../workload-limits';
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

  it('applies the workload limits of a signed-in session and the defaults otherwise', async () => {
    const limits = TestBed.inject(WorkloadLimits);
    const configured = { maxFilesPerRun: 500, maxStepsPerRun: 40, maxQueuedJobs: 8 };
    let loading = auth.refresh();
    http
      .expectOne('/api/auth/session')
      .flush({ username: 'admin', requiresSetup: false, limits: configured });
    await loading;
    expect(limits.current()).toEqual(configured);
    loading = auth.refresh();
    http
      .expectOne('/api/auth/session')
      .flush({ username: null, requiresSetup: false, limits: null });
    await loading;
    expect(limits.current()).toEqual(defaultLimits);
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

  it('signs out of the identity provider after a single sign-on session ends', async () => {
    const assign = vi.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authenticationInterceptor])),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: { cookie: '', location: { assign } } },
      ],
    });
    auth = TestBed.inject(Auth);
    http = TestBed.inject(HttpTestingController);
    auth.username.set('alice');

    const logout = auth.logout();
    http
      .expectOne('/api/auth/logout')
      .flush({ endSessionUrl: 'https://keycloak.test/logout?client_id=namerre' });
    await logout;

    expect(auth.username()).toBeNull();
    expect(assign).toHaveBeenCalledExactlyOnceWith(
      'https://keycloak.test/logout?client_id=namerre',
    );
  });

  it('stays in the app after a local sign-out', async () => {
    auth.username.set('admin');
    const logout = auth.logout();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await logout;
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

  it('changes the password with CSRF protection and reports whether it is possible', async () => {
    const change = auth.changePassword('old-password', 'new-password');
    http
      .expectOne('/api/auth/session')
      .flush({ username: 'admin', requiresSetup: false, canChangePassword: true });
    document.cookie = 'XSRF-TOKEN=change-csrf; Path=/';
    const request = await vi.waitFor(() => http.expectOne('/api/auth/password'));
    expect(auth.canChangePassword()).toBe(true);
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('change-csrf');
    expect(request.request.body).toEqual({
      currentPassword: 'old-password',
      newPassword: 'new-password',
    });
    request.flush(null);
    (await vi.waitFor(() => http.expectOne('/api/auth/session'))).flush({
      username: 'admin',
      requiresSetup: false,
      canChangePassword: true,
      role: 'Admin',
      userId: 1,
      hasPassword: true,
      logins: [],
    });
    await change;
    expect(auth.username()).toBe('admin');
    expect(auth.isAdmin()).toBe(true);
  });

  it('sets a first password without a current one and reads the sign-in methods', async () => {
    const change = auth.changePassword(null, 'a-first-password');
    http.expectOne('/api/auth/session').flush({ username: 'sso', requiresSetup: false });
    const request = await vi.waitFor(() => http.expectOne('/api/auth/password'));
    expect(request.request.body).toEqual({
      currentPassword: null,
      newPassword: 'a-first-password',
    });
    request.flush(null);
    const login = {
      id: 3,
      issuer: 'https://idp',
      displayName: 'sso',
      linkedAt: '2026-11-01T00:00:00Z',
      lastUsedAt: null,
    };
    (await vi.waitFor(() => http.expectOne('/api/auth/session'))).flush({
      username: 'sso',
      requiresSetup: false,
      role: 'User',
      hasPassword: true,
      recentSignIn: true,
      logins: [login],
    });
    await change;
    expect(auth.isAdmin()).toBe(false);
    expect(auth.hasPassword()).toBe(true);
    expect(auth.recentSignIn()).toBe(true);
    expect(auth.logins()).toEqual([login]);
  });

  it('starts linking single sign-on at the address the server returns', async () => {
    const assign = vi.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authenticationInterceptor])),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: { cookie: '', location: { assign } } },
      ],
    });
    auth = TestBed.inject(Auth);
    http = TestBed.inject(HttpTestingController);
    const link = auth.linkSingleSignOn('my-password');
    http.expectOne('/api/auth/session').flush({ username: 'bob', requiresSetup: false });
    const request = await vi.waitFor(() => http.expectOne('/api/auth/oidc/link'));
    expect(request.request.body).toEqual({ password: 'my-password' });
    request.flush({ url: '/api/auth/oidc/link?ticket=abc' });
    await link;
    expect(assign).toHaveBeenCalledWith('/api/auth/oidc/link?ticket=abc');
  });
});
