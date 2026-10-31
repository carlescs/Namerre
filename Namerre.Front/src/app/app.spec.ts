import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { App } from './app';
import { routes } from './app.routes';
import { Auth } from './auth/auth';

describe('App', () => {
  const username = signal<string | null>(null);
  const requiresSetup = signal(false);
  const canChangePassword = signal(true);
  const refresh = vi.fn<() => Promise<void>>();
  const logout = vi.fn<() => Promise<void>>();

  beforeEach(async () => {
    localStorage.clear();
    username.set(null);
    requiresSetup.set(false);
    refresh.mockReset().mockResolvedValue();
    logout.mockReset().mockImplementation(async () => username.set(null));
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideServiceWorker('ngsw-worker.js', { enabled: false }),
        {
          provide: Auth,
          useValue: {
            username,
            requiresSetup,
            canChangePassword,
            singleSignOn: signal(null),
            refresh,
            logout,
          },
        },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the lazy-loaded renamer at the root route', async () => {
    username.set('admin');
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-renamer h1')?.textContent).toBe('Renamer');
  });

  it('shows only the login screen to an anonymous visitor', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('app-login')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-renamer')).toBeNull();
  });

  it('shows the installation assistant only when the administrator is not configured', async () => {
    requiresSetup.set(true);
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('app-setup')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-login')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-renamer')).toBeNull();
    requiresSetup.set(false);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('app-setup')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-login')).not.toBeNull();
  });

  it('shows an explicit error and retry when session loading fails', async () => {
    refresh.mockRejectedValue(new Error('offline'));
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Unable to connect',
    );
    expect(fixture.nativeElement.querySelector('app-login')).toBeNull();
    expect(fixture.nativeElement.querySelector('button').textContent).toBe('Retry');
  });

  it('reconnects automatically when the network comes back', async () => {
    refresh.mockRejectedValueOnce(new Error('offline'));
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();

    window.dispatchEvent(new Event('online'));
    await fixture.whenStable();
    expect(refresh).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-login')).not.toBeNull();
  });

  it('says so when the device is offline', async () => {
    const onLine = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    try {
      refresh.mockRejectedValue(new Error('offline'));
      const fixture = TestBed.createComponent(App);
      await fixture.whenStable();
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        'you are offline',
      );
    } finally {
      onLine.mockRestore();
    }
  });

  it('offers the theme picker before and after signing in', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('app-theme-picker')).not.toBeNull();
    username.set('admin');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('header app-theme-picker')).not.toBeNull();
  });

  it('opens change password from the username menu in the header', async () => {
    username.set('admin');
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = element.querySelector('header app-user-menu button') as HTMLButtonElement;
    expect(trigger.textContent).toContain('admin');
    trigger.click();
    await fixture.whenStable();
    (element.querySelector('[role="menuitem"]') as HTMLAnchorElement).click();
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/account');
    expect(element.querySelector('[role="menu"]')).toBeNull();
    expect(element.querySelector('app-account h2')?.textContent).toBe('Change password');
  });

  it('shows the footer before and after signing in', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('footer')?.textContent).toContain('MIT License');
    username.set('admin');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('footer a')?.getAttribute('href')).toBe(
      'https://github.com/carlescs/Namerre',
    );
  });

  it('removes protected content after signing out', async () => {
    username.set('admin');
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    (
      fixture.nativeElement.querySelector('header app-user-menu button') as HTMLButtonElement
    ).click();
    await fixture.whenStable();
    const signOut = [...fixture.nativeElement.querySelectorAll('[role="menuitem"]')].find(
      (item: HTMLElement) => item.textContent?.trim() === 'Sign out',
    ) as HTMLButtonElement;
    signOut.click();
    await fixture.whenStable();
    expect(logout).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.querySelector('app-renamer')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-login')).not.toBeNull();
  });
});
