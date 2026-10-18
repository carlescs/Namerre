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
});
