import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';
import { Auth } from './auth/auth';

describe('App', () => {
  const username = signal<string | null>(null);
  const requiresSetup = signal(false);
  const refresh = vi.fn<() => Promise<void>>();
  const logout = vi.fn<() => Promise<void>>();

  beforeEach(async () => {
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
        { provide: Auth, useValue: { username, requiresSetup, refresh, logout } },
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

  it('removes protected content after signing out', async () => {
    username.set('admin');
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('header button') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(logout).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.querySelector('app-renamer')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-login')).not.toBeNull();
  });
});
