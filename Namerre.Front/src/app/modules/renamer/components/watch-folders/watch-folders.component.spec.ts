import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { type WatchFolderStatus, watchFolderOrigin } from '../../rename-api';
import { WatchFoldersComponent } from './watch-folders.component';

describe('WatchFoldersComponent', () => {
  let fixture: ComponentFixture<WatchFoldersComponent>;
  let http: HttpTestingController;
  const folder: WatchFolderStatus = {
    name: 'Downloads',
    path: 'downloads',
    preset: 'TV',
    paused: false,
    lastScanAt: '2026-10-31T12:00:00Z',
    inFlight: 1,
    completed: 4,
    error: null,
    problems: [
      {
        path: 'downloads/odd.mkv',
        status: 'Ignored',
        attempts: 5,
        error: 'The destination already exists and will not be overwritten.',
        nextAttemptAt: null,
        updatedAt: '2026-10-31T12:00:00Z',
      },
    ],
    tmdbAutoConfirm: 0.9,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WatchFoldersComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WatchFoldersComponent);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
  });

  it('renders nothing without configured folders', async () => {
    http.expectOne('/api/watch-folders').flush([]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });

  it('shows each folder with its counts and problems, and retries a file', async () => {
    http.expectOne('/api/watch-folders').flush([folder]);
    await fixture.whenStable();
    const text = () => (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
    expect(text()).toContain('Downloads');
    expect(text()).toContain('preset TV');
    expect(text()).toContain('Watching');
    expect(text()).toContain('4 renamed · 1 in progress');
    expect(text()).toContain('accepts TMDB suggestions from 90%');
    expect(text()).toContain('1 file was not renamed');
    expect(text()).toContain('Ignored after 5 attempts until the file changes.');
    button('Retry downloads/odd.mkv').click();
    const retry = http.expectOne('/api/watch-folders/Downloads/retry');
    expect(retry.request.body).toEqual({ path: 'downloads/odd.mkv' });
    retry.flush({ ...folder, problems: [] });
    await fixture.whenStable();
    expect(text()).not.toContain('not renamed');
  });

  it('pauses and resumes a folder and shows why one needs attention', async () => {
    http.expectOne('/api/watch-folders').flush([{ ...folder, error: 'Preset TV does not exist.' }]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Needs attention');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Preset TV does not exist.',
    );
    button('Pause Downloads').click();
    const pause = http.expectOne('/api/watch-folders/Downloads/pause');
    expect(pause.request.method).toBe('POST');
    pause.flush({ ...folder, paused: true });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Paused');
    button('Resume Downloads').click();
    http.expectOne('/api/watch-folders/Downloads/resume').flush(folder);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Watching');
  });

  it('reads the watch folder from an origin', () => {
    http.expectOne('/api/watch-folders').flush([]);
    expect(watchFolderOrigin('watch:Downloads')).toBe('Downloads');
    expect(watchFolderOrigin(null)).toBeNull();
    expect(watchFolderOrigin('other')).toBeNull();
  });

  function button(label: string): HTMLButtonElement {
    const result = fixture.nativeElement.querySelector(`button[aria-label="${label}"]`);
    if (!result) throw new Error(`Button not found: ${label}`);
    return result;
  }
});
