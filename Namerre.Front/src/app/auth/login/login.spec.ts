import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Auth } from '../auth';
import { Login } from './login';

describe('Login', () => {
  const login = vi.fn<(username: string, password: string) => Promise<void>>();

  beforeEach(async () => {
    login.mockReset().mockResolvedValue();
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [{ provide: Auth, useValue: { login } }],
    }).compileComponents();
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
