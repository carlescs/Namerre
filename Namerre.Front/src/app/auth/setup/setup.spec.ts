import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Auth } from '../auth';
import { Setup } from './setup';

describe('Setup', () => {
  const setup = vi.fn<(token: string, username: string, password: string) => Promise<void>>();

  beforeEach(async () => {
    setup.mockReset().mockResolvedValue();
    await TestBed.configureTestingModule({
      imports: [Setup],
      providers: [{ provide: Auth, useValue: { setup, singleSignOn: signal(null) } }],
    }).compileComponents();
  });

  function fill(element: HTMLElement, selector: string, value: string): void {
    const input = element.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('accepts a short password, requires confirmation, and submits administrator credentials', async () => {
    const fixture = TestBed.createComponent(Setup);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    fill(element, '#setup-token', 'a'.repeat(64));
    fill(element, '#setup-username', 'admin');
    fill(element, '#setup-password', 'x');
    fill(element, '#setup-confirmation', 'different-password');
    await fixture.whenStable();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Passwords must match');
    expect((element.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
    fill(element, '#setup-confirmation', 'x');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(setup).toHaveBeenCalledWith('a'.repeat(64), 'admin', 'x');
    expect((element.querySelector('#setup-password') as HTMLInputElement).value).toBe('');
    expect((element.querySelector('#setup-confirmation') as HTMLInputElement).value).toBe('');
  });

  it('still requires a non-empty password', async () => {
    const fixture = TestBed.createComponent(Setup);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    fill(element, '#setup-token', 'a'.repeat(64));
    fill(element, '#setup-username', 'admin');
    await fixture.whenStable();
    expect((element.querySelector('button') as HTMLButtonElement).disabled).toBe(true);
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(setup).not.toHaveBeenCalled();
  });

  it('shows database errors without claiming installation succeeded', async () => {
    setup.mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    const fixture = TestBed.createComponent(Setup);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    fill(element, '#setup-token', 'a'.repeat(64));
    fill(element, '#setup-username', 'admin');
    fill(element, '#setup-password', 'a-unique-long-password');
    fill(element, '#setup-confirmation', 'a-unique-long-password');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'database and permissions',
    );
  });
});
