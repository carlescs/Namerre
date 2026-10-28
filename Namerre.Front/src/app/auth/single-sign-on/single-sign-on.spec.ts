import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Auth, type SingleSignOnProvider } from '../auth';
import { SingleSignOn } from './single-sign-on';

describe('SingleSignOn', () => {
  const singleSignOn = signal<SingleSignOnProvider | null>(null);
  const startSingleSignOn = vi.fn();

  beforeEach(async () => {
    singleSignOn.set(null);
    startSingleSignOn.mockReset();
    await TestBed.configureTestingModule({
      imports: [SingleSignOn],
      providers: [{ provide: Auth, useValue: { singleSignOn, startSingleSignOn } }],
    }).compileComponents();
  });

  afterEach(() => history.replaceState(null, '', '/'));

  it('renders nothing when single sign-on is not configured', async () => {
    const fixture = TestBed.createComponent(SingleSignOn);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });

  it('starts sign-in with the configured identity provider', async () => {
    singleSignOn.set({ displayName: 'Keycloak' });
    const fixture = TestBed.createComponent(SingleSignOn);
    await fixture.whenStable();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.textContent).toContain('Sign in with Keycloak');
    button.click();
    await fixture.whenStable();
    expect(startSingleSignOn).toHaveBeenCalledOnce();
    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain('Redirecting...');
  });

  it.each([
    ['access-denied', 'Your account is not allowed to use Namerre.'],
    ['unavailable', 'The identity provider is unavailable.'],
    ['failed', 'Single sign-on failed. Try again.'],
    ['unexpected', 'Single sign-on failed. Try again.'],
  ])('explains a %s sign-in error and removes it from the address', async (code, message) => {
    history.replaceState(null, '', `/?signInError=${code}&keep=1#top`);
    const fixture = TestBed.createComponent(SingleSignOn);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(message);
    expect(location.pathname + location.search + location.hash).toBe('/?keep=1#top');
  });
});
