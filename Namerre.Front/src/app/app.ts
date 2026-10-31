import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, inject, type OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AppUpdate } from './app-update/app-update';
import { Auth } from './auth/auth';
import { Login } from './auth/login/login';
import { Setup } from './auth/setup/setup';
import { UserMenu } from './auth/user-menu/user-menu';
import { Logo } from './brand/logo';
import { SettingsMenu } from './settings-menu/settings-menu';
import { ThemePicker } from './theme/theme-picker';
import { WorkflowMode } from './workflow-mode/workflow-mode';
import { WorkflowModeWarning } from './workflow-mode/workflow-mode-warning';
import { WorkflowOnboarding } from './workflow-mode/workflow-onboarding';

@Component({
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    AppUpdate,
    Login,
    Setup,
    SettingsMenu,
    ThemePicker,
    UserMenu,
    Logo,
    WorkflowModeWarning,
    WorkflowOnboarding,
  ],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly auth = inject(Auth);
  protected readonly workflow = inject(WorkflowMode);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly signingOut = signal(false);
  private readonly window = inject(DOCUMENT).defaultView;

  constructor() {
    // The installed app opens from the cached shell even when the server is unreachable; retry the
    // session as soon as the network is back instead of leaving the user on the error.
    const reconnect = () => {
      if (this.error() && !this.loading() && !this.auth.username()) void this.initialize();
    };
    this.window?.addEventListener('online', reconnect);
    inject(DestroyRef).onDestroy(() => this.window?.removeEventListener('online', reconnect));
  }

  ngOnInit(): void {
    void this.initialize();
  }

  protected async initialize(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      await this.auth.refresh();
    } catch {
      this.error.set(
        this.window?.navigator.onLine === false
          ? 'Unable to connect to Namerre: you are offline. It will reconnect when the network is back.'
          : 'Unable to connect to Namerre. Check your connection and use HTTPS.',
      );
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
