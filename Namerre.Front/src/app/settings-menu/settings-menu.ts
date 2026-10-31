import {
  afterNextRender,
  Component,
  ElementRef,
  Injector,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faGear } from '@fortawesome/free-solid-svg-icons';
import { ThemePicker } from '../theme/theme-picker';
import { WorkflowModePicker } from '../workflow-mode/workflow-mode-picker';

/** Header popover holding the per-browser preferences: workflow mode and color theme. */
@Component({
  imports: [FaIconComponent, ThemePicker, WorkflowModePicker],
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
  protected readonly gearIcon = faGear;
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
