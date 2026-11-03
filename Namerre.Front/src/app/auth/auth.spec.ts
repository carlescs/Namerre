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

  it('signs in with a passkey: options, the browser prompt, then the answer and the session', async () => {
    const get = vi.fn().mockResolvedValue({
      id: 'Bw',
      rawId: new Uint8Array([7]).buffer,
      type: 'public-key',
      response: {
        clientDataJSON: new Uint8Array([1]).buffer,
        authenticatorData: new Uint8Array([2]).buffer,
        signature: new Uint8Array([3]).buffer,
        userHandle: null,
      },
      getClientExtensionResults: () => ({}),
    });
    Object.defineProperty(navigator, 'credentials', { value: { get }, configurable: true });
    const signingIn = auth.loginWithPasskey();
    http.expectOne('/api/auth/session').flush({ username: null, requiresSetup: false });
    await vi.waitFor(() =>
      http.expectOne('/api/auth/passkey/options').flush({
        options: { challenge: 'AAEC', rpId: 'localhost', allowCredentials: [] },
        ticket: 'ticket-1',
      }),
    );
    const answer = await vi.waitFor(() => http.expectOne('/api/auth/passkey/login'));
    expect(get.mock.calls[0]?.[0].publicKey.rpId).toBe('localhost');
    expect(answer.request.body).toEqual({
      ticket: 'ticket-1',
      credential: {
        id: 'Bw',
        rawId: 'Bw',
        type: 'public-key',
        response: { clientDataJSON: 'AQ', authenticatorData: 'Ag', signature: 'Aw' },
        clientExtensionResults: {},
      },
    });
    answer.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() =>
      http.expectOne('/api/auth/session').flush({
        username: 'alice',
        requiresSetup: false,
        passkeysEnabled: true,
        passkeys: [{ id: 1, name: 'Phone', createdAt: '2026-11-02T00:00:00Z', lastUsedAt: null }],
      }),
    );
    await signingIn;
    expect(auth.username()).toBe('alice');
    expect(auth.passkeysEnabled()).toBe(true);
    expect(auth.passkeys().map((p) => p.name)).toEqual(['Phone']);
  });

  it('registers a passkey with the name and the password confirming it', async () => {
    const create = vi.fn().mockResolvedValue({
      id: 'Bw',
      rawId: new Uint8Array([7]).buffer,
      type: 'public-key',
      response: {
        clientDataJSON: new Uint8Array([1]).buffer,
        attestationObject: new Uint8Array([2]).buffer,
        getTransports: () => ['internal'],
      },
      getClientExtensionResults: () => ({}),
    });
    Object.defineProperty(navigator, 'credentials', { value: { create }, configurable: true });
    const registering = auth.registerPasskey('  Laptop ', 'secret');
    http.expectOne('/api/auth/session').flush({ username: 'alice', requiresSetup: false });
    const options = await vi.waitFor(() => http.expectOne('/api/auth/passkeys/options'));
    expect(options.request.body).toEqual({ password: 'secret' });
    options.flush({
      options: {
        rp: { id: 'localhost', name: 'Namerre' },
        user: { id: 'AQI', name: 'alice', displayName: 'alice' },
        challenge: 'AAEC',
        pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
        excludeCredentials: [],
      },
      ticket: 'ticket-2',
    });
    const register = await vi.waitFor(() => http.expectOne('/api/auth/passkeys'));
    expect(register.request.body).toEqual({
      ticket: 'ticket-2',
      name: 'Laptop',
      credential: {
        id: 'Bw',
        rawId: 'Bw',
        type: 'public-key',
        response: { clientDataJSON: 'AQ', attestationObject: 'Ag', transports: ['internal'] },
        clientExtensionResults: {},
      },
    });
    register.flush([]);
    await vi.waitFor(() =>
      http.expectOne('/api/auth/session').flush({ username: 'alice', requiresSetup: false }),
    );
    await registering;
  });

  it('renames a passkey and keeps the list the server returns', async () => {
    const renaming = auth.renamePasskey(4, '  Work laptop ');
    http.expectOne('/api/auth/session').flush({ username: 'alice', requiresSetup: false });
    const rename = await vi.waitFor(() => http.expectOne('/api/auth/passkeys/4'));
    expect(rename.request.method).toBe('PATCH');
    expect(rename.request.body).toEqual({ name: 'Work laptop' });
    rename.flush([
      { id: 4, name: 'Work laptop', createdAt: '2026-11-02T00:00:00Z', lastUsedAt: null },
    ]);
    await renaming;
    expect(auth.passkeys().map((p) => p.name)).toEqual(['Work laptop']);
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
