import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Auth, type LinkedLogin } from '../auth';
import { Account } from './account';

describe('Account', () => {
  const changePassword = vi.fn<(current: string | null, next: string) => Promise<void>>();
  const linkSingleSignOn = vi.fn<(password?: string) => Promise<void>>();
  const reauthenticate = vi.fn<(password: string) => Promise<void>>();
  const removePassword = vi.fn<() => Promise<void>>();
  const unlink = vi.fn<(id: number) => Promise<void>>();
  const startSingleSignOn = vi.fn();
  const canChangePassword = signal(true);
  const hasPassword = signal(true);
  const managedByConfiguration = signal(false);
  const recentSignIn = signal(false);
  const logins = signal<LinkedLogin[]>([]);
  const singleSignOn = signal<{ displayName: string } | null>(null);
  const login: LinkedLogin = {
    id: 7,
    issuer: 'https://keycloak.test/realms/media',
    displayName: 'alice-sso',
    linkedAt: '2026-11-01T00:00:00Z',
    lastUsedAt: null,
  };

  beforeEach(async () => {
    for (const mock of [changePassword, linkSingleSignOn, reauthenticate, removePassword, unlink]) {
      mock.mockReset().mockResolvedValue();
    }
    startSingleSignOn.mockReset();
    canChangePassword.set(true);
    hasPassword.set(true);
    managedByConfiguration.set(false);
    recentSignIn.set(false);
    logins.set([]);
    singleSignOn.set(null);
    await TestBed.configureTestingModule({
      imports: [Account],
      providers: [
        {
          provide: Auth,
          useValue: {
            username: signal('admin'),
            role: signal('Admin'),
            canChangePassword,
            hasPassword,
            managedByConfiguration,
            recentSignIn,
            logins,
            singleSignOn,
            changePassword,
            linkSingleSignOn,
            reauthenticate,
            removePassword,
            unlink,
            startSingleSignOn,
          },
        },
      ],
    }).compileComponents();
  });

  function fill(element: HTMLElement, selector: string, value: string): void {
    const input = element.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submit(current: string, next: string, confirmation = next) {
    const fixture = TestBed.createComponent(Account);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    fill(element, '#current-password', current);
    fill(element, '#new-password', next);
    fill(element, '#new-password-confirmation', confirmation);
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    return element;
  }

  it('requires a matching confirmation before submitting', async () => {
    const element = await submit('old', 'a-new-password', 'different');
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Passwords must match');
    expect((element.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
    expect(changePassword).not.toHaveBeenCalled();
  });

  it('changes the password and clears the form', async () => {
    const element = await submit('old', 'a-new-password');
    expect(changePassword).toHaveBeenCalledWith('old', 'a-new-password');
    expect(element.querySelector('[role="status"]')?.textContent).toContain('Password changed');
    expect((element.querySelector('#current-password') as HTMLInputElement).value).toBe('');
    expect((element.querySelector('#new-password') as HTMLInputElement).value).toBe('');
  });

  it('reports an incorrect current password', async () => {
    changePassword.mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    const element = await submit('wrong', 'a-new-password');
    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'current password is incorrect',
    );
    expect(element.querySelector('[role="status"]')).toBeNull();
  });

  it('requires a new password of at least twelve characters', async () => {
    const element = await submit('old', 'eleven char');
    expect(element.querySelector('#new-password-hint')?.textContent).toContain(
      'At least 12 characters',
    );
    expect((element.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
    expect(changePassword).not.toHaveBeenCalled();
  });

  it('shows the server explanation for a rejected new password', async () => {
    changePassword.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: { message: 'Use a password of at least 12 characters.' },
      }),
    );
    const element = await submit('old', 'a-new-password');
    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'at least 12 characters',
    );
  });

  it('explains how to change server-configured credentials', async () => {
    canChangePassword.set(false);
    managedByConfiguration.set(true);
    const fixture = TestBed.createComponent(Account);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('form')).toBeNull();
    expect(element.textContent).toContain('Namerre:Authentication:PasswordHash');
  });

  async function render() {
    const fixture = TestBed.createComponent(Account);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function buttonNamed(element: HTMLElement, text: string): HTMLButtonElement {
    return [...element.querySelectorAll('button')].find((button) =>
      button.textContent?.includes(text),
    ) as HTMLButtonElement;
  }

  it('asks single sign-on users to sign in again before setting a first password', async () => {
    hasPassword.set(false);
    logins.set([login]);
    singleSignOn.set({ displayName: 'Keycloak' });
    const { element } = await render();
    expect(element.querySelector('h2')?.textContent).toContain('Set a password');
    expect(element.querySelector('form')).toBeNull();
    buttonNamed(element, 'Sign in again').click();
    expect(startSingleSignOn).toHaveBeenCalledWith('account');
  });

  it('sets a first password after a recent single sign-on without a current password', async () => {
    hasPassword.set(false);
    recentSignIn.set(true);
    logins.set([login]);
    const { fixture, element } = await render();
    expect(element.querySelector('#current-password')).toBeNull();
    fill(element, '#new-password', 'a-first-password');
    fill(element, '#new-password-confirmation', 'a-first-password');
    await fixture.whenStable();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(changePassword).toHaveBeenCalledWith(null, 'a-first-password');
  });

  it('never offers to unlink the last way to sign in', async () => {
    hasPassword.set(false);
    logins.set([login]);
    singleSignOn.set({ displayName: 'Keycloak' });
    const { element } = await render();
    expect(element.textContent).toContain('alice-sso');
    expect(
      (element.querySelector('[aria-label="Unlink alice-sso"]') as HTMLButtonElement).disabled,
    ).toBe(true);
  });

  it('confirms the password before linking single sign-on', async () => {
    singleSignOn.set({ displayName: 'Keycloak' });
    const { fixture, element } = await render();
    buttonNamed(element, 'Link Keycloak').click();
    await fixture.whenStable();
    expect(linkSingleSignOn).not.toHaveBeenCalled();
    fill(element, '#confirm-password', 'my-password');
    await fixture.whenStable();
    buttonNamed(element, 'Confirm').click();
    await fixture.whenStable();
    expect(linkSingleSignOn).toHaveBeenCalledWith('my-password');
  });

  it('links right away after a recent sign-in', async () => {
    singleSignOn.set({ displayName: 'Keycloak' });
    recentSignIn.set(true);
    const { fixture, element } = await render();
    buttonNamed(element, 'Link Keycloak').click();
    await fixture.whenStable();
    expect(linkSingleSignOn).toHaveBeenCalledWith(undefined);
  });

  it('confirms the password, then removes it when single sign-on is linked', async () => {
    singleSignOn.set({ displayName: 'Keycloak' });
    logins.set([login]);
    const { fixture, element } = await render();
    buttonNamed(element, 'Remove password').click();
    await fixture.whenStable();
    fill(element, '#confirm-password', 'my-password');
    await fixture.whenStable();
    buttonNamed(element, 'Confirm').click();
    await vi.waitFor(() => expect(removePassword).toHaveBeenCalled());
    expect(reauthenticate).toHaveBeenCalledWith('my-password');
    await fixture.whenStable();
    expect(element.textContent).toContain('Password removed');
  });
});
