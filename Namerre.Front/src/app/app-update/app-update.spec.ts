import { TestBed } from '@angular/core/testing';
import { SwUpdate, type UnrecoverableStateEvent, type VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { AppUpdate } from './app-update';

describe('AppUpdate', () => {
  let versionUpdates: Subject<VersionEvent>;
  let unrecoverable: Subject<UnrecoverableStateEvent>;
  let checkForUpdate: ReturnType<typeof vi.fn<() => Promise<boolean>>>;

  const ready = {
    type: 'VERSION_READY',
    currentVersion: { hash: 'old' },
    latestVersion: { hash: 'new' },
  } as VersionEvent;

  async function create(isEnabled = true) {
    versionUpdates = new Subject();
    unrecoverable = new Subject();
    checkForUpdate = vi.fn<() => Promise<boolean>>().mockResolvedValue(false);
    await TestBed.configureTestingModule({
      imports: [AppUpdate],
      providers: [
        {
          provide: SwUpdate,
          useValue: { isEnabled, versionUpdates, unrecoverable, checkForUpdate },
        },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(AppUpdate);
    await fixture.whenStable();
    return fixture;
  }

  it('stays hidden until a new version is ready', async () => {
    const fixture = await create();
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    versionUpdates.next({ type: 'VERSION_DETECTED', version: { hash: 'new' } });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
  });

  it('offers a reload when a new version is ready and can be postponed', async () => {
    const fixture = await create();
    versionUpdates.next(ready);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="status"]')?.textContent).toContain('new version');
    const buttons = [...element.querySelectorAll('button')].map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['Reload', 'Later']);

    (element.querySelectorAll('button')[1] as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(element.querySelector('[role="status"]')).toBeNull();
  });

  it('asks for a reload when the cached version is unrecoverable', async () => {
    const fixture = await create();
    unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing asset' });
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="status"]')?.textContent).toContain('Reload to recover');
    expect([...element.querySelectorAll('button')].map((b) => b.textContent?.trim())).toEqual([
      'Reload',
    ]);
  });

  it('checks for a new version when the window becomes visible again', async () => {
    await create();
    document.dispatchEvent(new Event('visibilitychange'));
    expect(checkForUpdate).toHaveBeenCalledOnce();
  });

  it('does nothing when the service worker is disabled', async () => {
    const fixture = await create(false);
    versionUpdates.next(ready);
    document.dispatchEvent(new Event('visibilitychange'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    expect(checkForUpdate).not.toHaveBeenCalled();
  });
});
