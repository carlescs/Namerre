import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Auth } from '../auth';
import { Account } from './account';

describe('Account', () => {
  const changePassword = vi.fn<(current: string, next: string) => Promise<void>>();
  const canChangePassword = signal(true);

  beforeEach(async () => {
    changePassword.mockReset().mockResolvedValue();
    canChangePassword.set(true);
    await TestBed.configureTestingModule({
      imports: [Account],
      providers: [
        {
          provide: Auth,
          useValue: { username: signal('admin'), canChangePassword, changePassword },
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
    const fixture = TestBed.createComponent(Account);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('form')).toBeNull();
    expect(element.textContent).toContain('Namerre:Authentication:PasswordHash');
  });
});
