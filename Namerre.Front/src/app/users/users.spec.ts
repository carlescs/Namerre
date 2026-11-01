import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Auth } from '../auth/auth';
import { Users } from './users';
import type { UserView } from './users-api';

describe('Users', () => {
  let http: HttpTestingController;
  const admin: UserView = {
    id: 1,
    username: 'admin',
    role: 'Admin',
    disabled: false,
    hasPassword: true,
    managedByConfiguration: false,
    createdAt: '2026-11-01T00:00:00Z',
    lastSignInAt: null,
    logins: [],
  };
  const alice: UserView = {
    ...admin,
    id: 2,
    username: 'alice',
    role: 'User',
    hasPassword: false,
    logins: [
      {
        id: 5,
        issuer: 'https://keycloak.test/realms/media',
        displayName: 'alice',
        linkedAt: '2026-11-01T00:00:00Z',
        lastUsedAt: null,
      },
    ],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Users],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    const auth = TestBed.inject(Auth);
    auth.role.set('Admin');
    auth.userId.set(1);
    auth.singleSignOn.set({ displayName: 'Keycloak' });
  });

  afterEach(() => http.verify());

  async function settle(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  async function render() {
    const fixture = TestBed.createComponent(Users);
    await fixture.whenStable();
    http.expectOne('/api/users').flush([admin, alice]);
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function fill(element: HTMLElement, selector: string, value: string): void {
    const input = element.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function buttonNamed(element: HTMLElement, text: string): HTMLButtonElement {
    return [...element.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === text,
    ) as HTMLButtonElement;
  }

  it('lists users with their roles and sign-in methods', async () => {
    const { element } = await render();
    const rows = [...element.querySelectorAll('tbody tr')].map((row) =>
      row.textContent?.replace(/\s+/g, ' ').trim(),
    );
    expect(rows[0]).toMatch(/admin\s*\(you\)/);
    expect(rows[0]).toContain('Administrator');
    expect(rows[0]).toContain('Password');
    expect(rows[1]).toContain('alice');
    expect(rows[1]).toContain('User');
  });

  it('creates a user with a password and reloads the list', async () => {
    const { fixture, element } = await render();
    const dialog = element.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.hasAttribute('open')).toBe(false);
    buttonNamed(element, 'Add user').click();
    await fixture.whenStable();
    expect(dialog.hasAttribute('open')).toBe(true);
    fill(element, '#new-username', 'bob');
    fill(element, '#new-user-password', 'bob-long-password');
    await fixture.whenStable();
    dialog.querySelector('form')!.dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/users');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      username: 'bob',
      role: 'User',
      password: 'bob-long-password',
      subject: null,
      displayName: null,
    });
    request.flush({ ...admin, id: 3, username: 'bob', role: 'User' });
    await settle(fixture);
    http.expectOne('/api/users').flush([admin, alice]);
    await settle(fixture);
    expect(element.querySelector('[role="status"]')?.textContent).toContain('Created bob');
    expect(dialog.hasAttribute('open')).toBe(false);
  });

  it('keeps the dialog open and explains a refused new user inside it', async () => {
    const { fixture, element } = await render();
    const dialog = element.querySelector('dialog') as HTMLDialogElement;
    buttonNamed(element, 'Add user').click();
    fill(element, '#new-username', 'alice');
    fill(element, '#new-user-password', 'alice-long-password');
    await fixture.whenStable();
    dialog.querySelector('form')!.dispatchEvent(new Event('submit'));
    http
      .expectOne('/api/users')
      .flush(
        { code: 'UsernameTaken', message: 'That username is already taken.' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle(fixture);
    http.expectOne('/api/users').flush([admin, alice]);
    await settle(fixture);
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain('already taken');

    buttonNamed(element, 'Cancel').click();
    await fixture.whenStable();
    expect(dialog.hasAttribute('open')).toBe(false);
  });

  it('keeps each user on one line on small screens', async () => {
    const { element } = await render();
    const [adminRow] = element.querySelectorAll('tbody tr');
    expect(adminRow.querySelector('td strong')?.classList).toContain('truncate');
    expect(adminRow.querySelector('.badge')?.textContent).toBe('Admin');
    expect(adminRow.textContent).toContain('Password');
  });

  it('keeps administrators from changing their own role or deleting themselves', async () => {
    const { fixture, element } = await render();
    (element.querySelector('[aria-label="Manage admin"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect((element.querySelector('#user-role') as HTMLSelectElement).disabled).toBe(true);
    expect(buttonNamed(element, 'Disable').disabled).toBe(true);
    expect(buttonNamed(element, 'Delete user').disabled).toBe(true);
    // Their own password needs the Account page's confirmation.
    expect(element.querySelector('#user-password')).toBeNull();
    expect(element.querySelector('a[href="/account"]')).not.toBeNull();
  });

  it('promotes a user and shows refusals from the server', async () => {
    const { fixture, element } = await render();
    (element.querySelector('[aria-label="Manage alice"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(
      (element.querySelector('[aria-label="Unlink alice"]') as HTMLButtonElement).disabled,
    ).toBe(true);
    const role = element.querySelector('#user-role') as HTMLSelectElement;
    role.value = 'Admin';
    role.dispatchEvent(new Event('change'));
    const promote = http.expectOne('/api/users/2');
    expect(promote.request.method).toBe('PATCH');
    expect(promote.request.body).toEqual({ role: 'Admin' });
    promote.flush(
      { code: 'UserChanged', message: 'The user changed while you were editing.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle(fixture);
    http.expectOne('/api/users').flush([admin, alice]);
    await settle(fixture);
    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'changed while you were editing',
    );
  });

  it('links a single sign-on identity by subject', async () => {
    const { fixture, element } = await render();
    (element.querySelector('[aria-label="Manage admin"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fill(element, '#link-subject', 'subject-9');
    await fixture.whenStable();
    buttonNamed(element, 'Link identity').click();
    const link = http.expectOne('/api/users/1/logins');
    expect(link.request.body).toEqual({ subject: 'subject-9', displayName: null });
    link.flush(admin);
    await settle(fixture);
    http.expectOne('/api/users').flush([admin, alice]);
  });

  it('merges the user an identity comes from, with its identities and run history', async () => {
    const { fixture, element } = await render();
    (element.querySelector('[aria-label="Manage admin"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const select = element.querySelector('#move-login') as HTMLSelectElement;
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    const merge = element.querySelector('input[type="radio"][value="merge"]') as HTMLInputElement;
    merge.click();
    await fixture.whenStable();
    buttonNamed(element, 'Merge user').click();
    const request = http.expectOne('/api/users/1/merge');
    expect(request.request.body).toEqual({ sourceUserId: 2 });
    request.flush({ ...admin, logins: alice.logins });
    await settle(fixture);
    http.expectOne('/api/users').flush([{ ...admin, logins: alice.logins }]);
    await settle(fixture);
    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Merged alice into admin',
    );
  });
});
