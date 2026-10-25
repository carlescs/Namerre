import {
  afterNextRender,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faChevronDown, faKey } from '@fortawesome/free-solid-svg-icons';

@Component({
  imports: [RouterLink, FaIconComponent],
  selector: 'app-user-menu',
  styleUrl: './user-menu.scss',
  templateUrl: './user-menu.html',
  host: {
    '(document:click)': 'closeOnOutsideClick($event)',
    '(keydown.escape)': 'close(true)',
  },
})
export class UserMenu {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  readonly username = input.required<string>();
  protected readonly open = signal(false);
  protected readonly chevronIcon = faChevronDown;
  protected readonly keyIcon = faKey;
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly firstItem = viewChild<ElementRef<HTMLElement>>('firstItem');

  protected toggle(): void {
    this.open.update((open) => !open);
    if (this.open()) {
      afterNextRender(() => this.firstItem()?.nativeElement.focus(), { injector: this.injector });
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
