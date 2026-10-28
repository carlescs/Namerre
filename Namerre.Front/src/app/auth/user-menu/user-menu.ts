import {
  afterNextRender,
  Component,
  ElementRef,
  Injector,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faChevronDown, faKey, faRightFromBracket } from '@fortawesome/free-solid-svg-icons';

@Component({
  imports: [RouterLink, FaIconComponent],
  selector: 'app-user-menu',
  styleUrl: './user-menu.scss',
  templateUrl: './user-menu.html',
  host: {
    '(document:click)': 'closeOnOutsideClick($event)',
    '(keydown.escape)': 'close(true)',
    '(keydown.arrowdown)': 'moveFocus($event, 1)',
    '(keydown.arrowup)': 'moveFocus($event, -1)',
  },
})
export class UserMenu {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  readonly username = input.required<string>();
  readonly signingOut = input(false);
  readonly signOut = output();
  protected readonly open = signal(false);
  protected readonly chevronIcon = faChevronDown;
  protected readonly keyIcon = faKey;
  protected readonly signOutIcon = faRightFromBracket;
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

  protected requestSignOut(): void {
    this.close();
    this.signOut.emit();
  }

  protected moveFocus(event: Event, step: number): void {
    const items = [
      ...this.host.nativeElement.querySelectorAll<HTMLElement>('[role="menuitem"]:not(:disabled)'),
    ];
    if (!items.length) return;
    event.preventDefault();
    const current = items.indexOf(document.activeElement as HTMLElement);
    items[(current + step + items.length) % items.length].focus();
  }

  protected closeOnOutsideClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.close();
  }
}
