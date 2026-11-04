import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Auth } from '../auth';
import { Login } from './login';

describe('Login', () => {
  const login = vi.fn<(username: string, password: string) => Promise<void>>();
  const loginWithPasskey = vi.fn<() => Promise<void>>();
  const passkeysEnabled = signal(false);

  beforeEach(async () => {
    login.mockReset().mockResolvedValue();
    loginWithPasskey.mockReset().mockResolvedValue();
    passkeysEnabled.set(false);
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        {
          provide: Auth,
          useValue: { login, loginWithPasskey, passkeysEnabled, singleSignOn: signal(null) },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => vi.unstubAllGlobals());

  function passkeyButton(fixture: { nativeElement: HTMLElement }): HTMLButtonElement | undefined {
    return [...fixture.nativeElement.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Sign in with a passkey',
    );
  }

  function supportPasskeys(): void {
    vi.stubGlobal('isSecureContext', true);
    vi.stubGlobal('PublicKeyCredential', class {});
    Object.defineProperty(navigator, 'credentials', {
      value: { get: vi.fn(), create: vi.fn() },
      configurable: true,
    });
  }

  it('offers passkeys only when the server and the browser support them', async () => {
    let fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    expect(passkeyButton(fixture)).toBeUndefined();

    passkeysEnabled.set(true);
    supportPasskeys();
    fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    expect(passkeyButton(fixture)).toBeDefined();
  });

  it.each([
    [new HttpErrorResponse({ status: 401 }), 'That passkey was not accepted.'],
    [new HttpErrorResponse({ status: 429 }), 'Too many sign-in attempts.'],
    [new Error('offline'), 'Unable to sign in with a passkey.'],
  ])('signs in with a passkey and explains %s', async (failure, message) => {
    passkeysEnabled.set(true);
    supportPasskeys();
    loginWithPasskey.mockRejectedValue(failure);
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    passkeyButton(fixture)!.click();
    await fixture.whenStable();
    expect(loginWithPasskey).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(message);
  });

  it('says nothing when the passkey prompt is cancelled', async () => {
    passkeysEnabled.set(true);
    supportPasskeys();
    loginWithPasskey.mockRejectedValue(new DOMException('closed', 'NotAllowedError'));
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    passkeyButton(fixture)!.click();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('submits credentials and clears the password after a failed login', async () => {
    login.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    const username = fixture.nativeElement.querySelector('#username') as HTMLInputElement;
    const password = fixture.nativeElement.querySelector('#password') as HTMLInputElement;
    username.value = 'admin';
    username.dispatchEvent(new Event('input'));
    password.value = 'incorrect';
    password.dispatchEvent(new Event('input'));
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(login).toHaveBeenCalledWith('admin', 'incorrect');
    expect(password.value).toBe('');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Invalid username or password.',
    );
  });

  it.each([
    [429, 'Too many sign-in attempts. Wait one minute and try again.'],
    [500, 'Unable to sign in. Check your connection and use HTTPS, then try again.'],
    [0, 'Unable to sign in. Check your connection and use HTTPS, then try again.'],
  ])('explains a %s sign-in failure', async (status, message) => {
    login.mockRejectedValue(new HttpErrorResponse({ status }));
    const fixture = await filledForm('admin', 'secret');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(message);
  });

  it.each([
    [429, 'SignInThrottled', 'Too many failed sign-ins for this account. Wait a minute'],
    [403, 'PasskeyRequired', 'Administrators sign in with a passkey or single sign-on.'],
    [403, 'Other', 'Unable to sign in.'],
  ])('explains a %s %s refusal', async (status, code, message) => {
    login.mockRejectedValue(new HttpErrorResponse({ status, error: { code } }));
    const fixture = await filledForm('admin', 'secret');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(message);
  });

  it('explains unexpected sign-in failures', async () => {
    login.mockRejectedValue(new Error('offline'));
    const fixture = await filledForm('admin', 'secret');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Unable to sign in.',
    );
  });

  it('does not submit an incomplete form', async () => {
    const fixture = await filledForm('admin', '');
    const submit = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(login).not.toHaveBeenCalled();
  });

  it('ignores repeated submissions while signing in', async () => {
    let finish!: () => void;
    login.mockReturnValue(new Promise<void>((resolve) => (finish = resolve)));
    const fixture = await filledForm('admin', 'secret');
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').textContent).toContain(
      'Signing in...',
    );
    form.dispatchEvent(new Event('submit'));
    expect(login).toHaveBeenCalledTimes(1);
    finish();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  async function filledForm(name: string, secret: string) {
    const fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
    const username = fixture.nativeElement.querySelector('#username') as HTMLInputElement;
    const password = fixture.nativeElement.querySelector('#password') as HTMLInputElement;
    username.value = name;
    username.dispatchEvent(new Event('input'));
    password.value = secret;
    password.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    return fixture;
  }
});
