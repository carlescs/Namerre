import { DOCUMENT } from '@angular/common';
import { DestroyRef, effect, Injectable, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

// Keep in sync with the pre-boot script in src/index.html.
export const themeStorageKey = 'namerre-theme';
// The header background (white / slate-900), used for the installed app's title bar.
export const themeColors = { light: '#ffffff', dark: '#0f172a' } as const;

const preferences: readonly ThemePreference[] = ['system', 'light', 'dark'];

@Injectable({ providedIn: 'root' })
export class Theme {
  private readonly document = inject(DOCUMENT);
  private readonly media = this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.media?.matches ?? false);
  readonly preference = signal<ThemePreference>(this.readPreference());

  constructor() {
    const listener = (event: MediaQueryListEvent) => this.systemDark.set(event.matches);
    this.media?.addEventListener?.('change', listener);
    inject(DestroyRef).onDestroy(() => this.media?.removeEventListener?.('change', listener));
    effect(() => {
      const preference = this.preference();
      const dark = preference === 'dark' || (preference === 'system' && this.systemDark());
      const root = this.document.documentElement;
      root.classList.toggle('dark', dark);
      root.style.colorScheme = dark ? 'dark' : 'light';
      this.document
        .querySelector('meta[name="theme-color"]')
        ?.setAttribute('content', dark ? themeColors.dark : themeColors.light);
    });
  }

  select(preference: ThemePreference): void {
    this.preference.set(preference);
    try {
      if (preference === 'system') {
        localStorage.removeItem(themeStorageKey);
      } else {
        localStorage.setItem(themeStorageKey, preference);
      }
    } catch {
      // Storage can be unavailable (private mode, blocked site data); the choice still applies.
    }
  }

  private readPreference(): ThemePreference {
    try {
      const stored = localStorage.getItem(themeStorageKey) as ThemePreference | null;
      return stored && preferences.includes(stored) ? stored : 'system';
    } catch {
      return 'system';
    }
  }
}
