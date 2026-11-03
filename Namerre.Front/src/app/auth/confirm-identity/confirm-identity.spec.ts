import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Auth, type LinkedLogin, type Passkey } from '../auth';
import { ConfirmIdentity } from './confirm-identity';

describe('ConfirmIdentity', () => {
  const reauthenticate = vi.fn<(password: string) => Promise<void>>();
  const reauthenticateWithPasskey = vi.fn<() => Promise<void>>();
  const startSingleSignOn = vi.fn();
  const hasPassword = signal(true);
  const passkeysEnabled = signal(false);
  const passkeys = signal<Passkey[]>([]);
  const logins = signal<LinkedLogin[]>([]);
  const singleSignOn = signal<{ displayName: string } | null>(null);

  beforeEach(async () => {
    reauthenticate.mockReset().mockResolvedValue();
    reauthenticateWithPasskey.mockReset().mockResolvedValue();
    startSingleSignOn.mockReset();
    hasPassword.set(true);
    passkeysEnabled.set(false);
    passkeys.set([]);
    logins.set([]);
    singleSignOn.set(null);
    await TestBed.configureTestingModule({
      imports: [ConfirmIdentity],
      providers: [
        {
          provide: Auth,
          useValue: {
            hasPassword,
            passkeysEnabled,
            passkeys,
            logins,
            singleSignOn,
            reauthenticate,
            reauthenticateWithPasskey,
            startSingleSignOn,
          },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => vi.unstubAllGlobals());

  async function render() {
    const fixture = TestBed.createComponent(ConfirmIdentity);
    const confirmed = vi.fn();
    const cancelled = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentInstance.cancelled.subscribe(cancelled);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement, confirmed, cancelled };
  }

  function buttonNamed(element: HTMLElement, text: string): HTMLButtonElement | undefined {
    return [...element.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === text,
    );
  }

  async function submitPassword(
    fixture: { whenStable(): Promise<unknown> },
    element: HTMLElement,
    password: string,
  ): Promise<void> {
    const input = element.querySelector('#confirm-identity-password') as HTMLInputElement;
    input.value = password;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('confirms with the password and reports it', async () => {
    const { fixture, element, confirmed } = await render();
    await submitPassword(fixture, element, 'the-password');
    expect(reauthenticate).toHaveBeenCalledWith('the-password');
    expect(confirmed).toHaveBeenCalledTimes(1);
  });

  it('explains a wrong password and does not report a confirmation', async () => {
    reauthenticate.mockRejectedValue(
      new HttpErrorResponse({
        status: 403,
        error: { code: 'InvalidPassword', message: 'The password is incorrect.' },
      }),
    );
    const { fixture, element, confirmed } = await render();
    await submitPassword(fixture, element, 'wrong');
    await fixture.whenStable();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('incorrect');
    expect(confirmed).not.toHaveBeenCalled();
  });

  it('reports cancelling', async () => {
    const { element, cancelled } = await render();
    buttonNamed(element, 'Cancel')!.click();
    expect(cancelled).toHaveBeenCalledTimes(1);
  });

  it('offers single sign-on to users without a password and returns to the users page', async () => {
    hasPassword.set(false);
    singleSignOn.set({ displayName: 'Keycloak' });
    logins.set([
      {
        id: 7,
        issuer: 'https://keycloak.test/realms/media',
        displayName: 'alice',
        linkedAt: '2026-11-01T00:00:00Z',
        lastUsedAt: null,
      },
    ]);
    const { fixture, element } = await render();
    expect(element.querySelector('input[type="password"]')).toBeNull();
    buttonNamed(element, 'Sign in again')!.click();
    await fixture.whenStable();
    expect(startSingleSignOn).toHaveBeenCalledWith('users');
  });

  it('confirms with a passkey when the user has one', async () => {
    hasPassword.set(false);
    passkeysEnabled.set(true);
    passkeys.set([{ id: 3, name: 'Phone', createdAt: '2026-11-02T00:00:00Z', lastUsedAt: null }]);
    vi.stubGlobal('isSecureContext', true);
    vi.stubGlobal('PublicKeyCredential', class {});
    Object.defineProperty(navigator, 'credentials', {
      value: { get: vi.fn(), create: vi.fn() },
      configurable: true,
    });
    const { element, confirmed, fixture } = await render();
    buttonNamed(element, 'Use a passkey')!.click();
    await fixture.whenStable();
    expect(reauthenticateWithPasskey).toHaveBeenCalledTimes(1);
    expect(confirmed).toHaveBeenCalledTimes(1);
  });
});
