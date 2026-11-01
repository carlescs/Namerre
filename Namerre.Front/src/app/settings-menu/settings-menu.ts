import {
  afterNextRender,
  Component,
  ElementRef,
  Injector,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faSliders, faUsers } from '@fortawesome/free-solid-svg-icons';
import { Auth } from '../auth/auth';
import { ThemePicker } from '../theme/theme-picker';
import { WorkflowModePicker } from '../workflow-mode/workflow-mode-picker';

/**
 * Header popover holding the per-browser preferences (workflow mode and color theme) and, for
 * administrators, the link to user management.
 */
@Component({
  imports: [FaIconComponent, RouterLink, ThemePicker, WorkflowModePicker],
  selector: 'app-settings-menu',
  styleUrl: './settings-menu.scss',
  templateUrl: './settings-menu.html',
  host: {
    '(document:click)': 'closeOnOutsideClick($event)',
    '(keydown.escape)': 'close(true)',
  },
})
export class SettingsMenu {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly open = signal(false);
  protected readonly auth = inject(Auth);
  protected readonly optionsIcon = faSliders;
  protected readonly usersIcon = faUsers;
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');

  protected toggle(): void {
    this.open.update((open) => !open);
    if (this.open()) {
      afterNextRender(
        () =>
          this.panel()?.nativeElement.querySelector<HTMLElement>('[aria-pressed="true"]')?.focus(),
        { injector: this.injector },
      );
    }
  }

  protected close(restoreFocus = false): void {
    if (!this.open()) return;
    this.open.set(false);
    if (restoreFocus) this.trigger().nativeElement.focus();
  }

  protected closeOnOutsideClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.close();
  }
}
