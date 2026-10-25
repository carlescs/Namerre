import { TestBed } from '@angular/core/testing';
import { Theme } from './theme';
import { ThemePicker } from './theme-picker';

describe('ThemePicker', () => {
  afterEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it('marks the active preference and selects another one', async () => {
    const fixture = TestBed.createComponent(ThemePicker);
    await fixture.whenStable();
    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    expect(buttons.map((button) => button.getAttribute('aria-label'))).toEqual([
      'Light theme',
      'Dark theme',
      'Match system theme',
    ]);
    expect(buttons[2].getAttribute('aria-pressed')).toBe('true');

    buttons[1].click();
    await fixture.whenStable();
    expect(TestBed.inject(Theme).preference()).toBe('dark');
    expect(buttons[1].getAttribute('aria-pressed')).toBe('true');
    expect(buttons[2].getAttribute('aria-pressed')).toBe('false');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
  });
});
