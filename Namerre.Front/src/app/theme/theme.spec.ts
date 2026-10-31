import { TestBed } from '@angular/core/testing';
import { Theme, themeColors, themeStorageKey } from './theme';

describe('Theme', () => {
  let systemDark: boolean;
  let listeners: ((event: MediaQueryListEvent) => void)[];
  const root = document.documentElement;

  beforeEach(() => {
    systemDark = false;
    listeners = [];
    localStorage.clear();
    root.classList.remove('dark');
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({
        get matches() {
          return systemDark;
        },
        addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) =>
          listeners.push(listener),
        removeEventListener: vi.fn(),
      })),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
    root.classList.remove('dark');
  });

  function create(): Theme {
    const theme = TestBed.inject(Theme);
    TestBed.tick();
    return theme;
  }

  it('follows the system preference by default', () => {
    systemDark = true;
    const theme = create();
    expect(theme.preference()).toBe('system');
    expect(root.classList.contains('dark')).toBe(true);
    expect(root.style.colorScheme).toBe('dark');

    for (const listener of listeners) listener({ matches: false } as MediaQueryListEvent);
    TestBed.tick();
    expect(root.classList.contains('dark')).toBe(false);
    expect(root.style.colorScheme).toBe('light');
  });

  it('applies and persists an explicit choice regardless of the system', () => {
    systemDark = true;
    const theme = create();
    theme.select('light');
    TestBed.tick();
    expect(root.classList.contains('dark')).toBe(false);
    expect(localStorage.getItem(themeStorageKey)).toBe('light');

    theme.select('dark');
    TestBed.tick();
    expect(root.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem(themeStorageKey)).toBe('dark');

    theme.select('system');
    TestBed.tick();
    expect(localStorage.getItem(themeStorageKey)).toBeNull();
    expect(root.classList.contains('dark')).toBe(true);
  });

  it('matches the installed app title bar to the theme', () => {
    const meta = document.createElement('meta');
    meta.name = 'theme-color';
    document.head.append(meta);
    try {
      const theme = create();
      expect(meta.content).toBe(themeColors.light);
      theme.select('dark');
      TestBed.tick();
      expect(meta.content).toBe(themeColors.dark);
    } finally {
      meta.remove();
    }
  });

  it('restores a saved choice and ignores unknown values', () => {
    localStorage.setItem(themeStorageKey, 'dark');
    expect(create().preference()).toBe('dark');
    expect(root.classList.contains('dark')).toBe(true);

    TestBed.resetTestingModule();
    localStorage.setItem(themeStorageKey, 'sepia');
    expect(create().preference()).toBe('system');
  });
});
