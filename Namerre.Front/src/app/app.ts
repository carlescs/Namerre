import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from './auth/auth';
import { Login } from './auth/login/login';
import { Setup } from './auth/setup/setup';
import { ThemePicker } from './theme/theme-picker';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Login, Setup, ThemePicker],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly auth = inject(Auth);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly signingOut = signal(false);

  ngOnInit(): void {
    void this.initialize();
  }

  protected async initialize(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      await this.auth.refresh();
    } catch {
      this.error.set('Unable to connect to Namerre. Check your connection and use HTTPS.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async logout(): Promise<void> {
    this.signingOut.set(true);
    this.error.set('');
    try {
      await this.auth.logout();
    } catch {
      this.error.set('Unable to sign out. Please try again.');
    } finally {
      this.signingOut.set(false);
    }
  }
}
