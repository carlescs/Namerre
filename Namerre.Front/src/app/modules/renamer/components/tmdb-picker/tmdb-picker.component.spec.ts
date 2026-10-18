import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { optionsHash } from '../../options-hash';
import { MetadataSelection, TmdbPickerState } from '../../rename-api';
import { TmdbPickerComponent } from './tmdb-picker.component';

describe('TmdbPickerComponent', () => {
  let fixture: ComponentFixture<TmdbPickerComponent>;
  let http: HttpTestingController;
  const plugin = {
    instanceId: 'tmdb-2',
    typeId: 'tmdb',
    version: 1,
    enabled: true,
    options: { nameTemplate: '{title} - S{season}E{episode} - {episodeTitle}', language: 'en-US' },
  };
  const show = {
    id: 42,
    mediaType: 'tv',
    title: 'A Show',
    year: '2020',
    season: null,
    episode: null,
    episodeTitle: null,
    confirmationToken: null,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TmdbPickerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    // jsdom does not implement the modal dialog lifecycle.
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
    fixture = TestBed.createComponent(TmdbPickerComponent);
    fixture.componentRef.setInput('context', {
      fileId: 'file-1',
      plugin,
      inputPath: 'TV/candidate.mkv',
    });
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
    vi.restoreAllMocks();
  });

  it('requires candidate selection, server verification and explicit episode confirmation', async () => {
    const confirmed = vi.fn<(selection: MetadataSelection) => void>();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    field('mediaType', 'tv', 'change');
    await fixture.whenStable();
    field('season', '2');
    field('episode', '3');
    await fixture.whenStable();
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    const search = http.expectOne('/api/metadata/tmdb/search');
    expect(search.request.body).toMatchObject({
      mediaType: 'tv',
      query: 'candidate',
    });
    expect(search.request.body.season).toBeUndefined();
    expect(search.request.body.episode).toBeUndefined();
    expect(search.request.body.id).toBeUndefined();
    search.flush({ matches: [show] });
    await fixture.whenStable();
    expect(confirmed).not.toHaveBeenCalled();
    expect(button('Verify chosen').disabled).toBe(true);
    (fixture.nativeElement.querySelector('input[type="radio"]') as HTMLInputElement).click();
    await fixture.whenStable();
    field('episode', '4');
    await fixture.whenStable();
    expect(button('Verify chosen').disabled).toBe(false);
    button('Verify chosen').click();
    const verify = http.expectOne('/api/metadata/tmdb/search');
    expect(verify.request.body).toMatchObject({
      id: 42,
      fileId: 'file-1',
      instanceId: 'tmdb-2',
      inputPath: 'TV/candidate.mkv',
      optionsHash: optionsHash(JSON.stringify(plugin.options)),
      season: 2,
      episode: 4,
    });
    verify.flush({
      matches: [
        {
          ...show,
          season: 2,
          episode: 4,
          episodeTitle: 'Exact Episode',
          confirmationToken: 'signed-snapshot',
        },
      ],
    });
    await fixture.whenStable();
    expect(confirmed).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Exact Episode');
    button('Confirm this match').click();
    expect(confirmed).toHaveBeenCalledWith({
      fileId: 'file-1',
      instanceId: 'tmdb-2',
      inputPath: 'TV/candidate.mkv',
      optionsHash: optionsHash(JSON.stringify(plugin.options)),
      confirmationToken: 'signed-snapshot',
    });
  });

  it('cancels a stale lookup and clears approval when the search is corrected', async () => {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    const pending = http.expectOne('/api/metadata/tmdb/search');
    field('query', 'Corrected title');
    await fixture.whenStable();
    expect(pending.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('Confirm this match');
  });

  it('restores the show and episode without restoring approval for the file', async () => {
    fixture.destroy();
    fixture = TestBed.createComponent(TmdbPickerComponent);
    fixture.componentRef.setInput('context', {
      fileId: 'file-2',
      plugin,
      inputPath: 'TV/other.mkv',
      state: {
        query: 'A Show',
        mediaType: 'tv',
        language: 'fr',
        season: 2,
        episode: 4,
        matches: [show],
        selected: show,
      },
    });
    await fixture.whenStable();
    expect(control('mediaType').value).toBe('tv');
    expect(control('query').value).toBe('A Show');
    expect(control('language').value).toBe('fr');
    expect(control('season').value).toBe('2');
    expect(control('episode').value).toBe('4');
    expect(fixture.nativeElement.querySelector('input[type="radio"]').checked).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('Confirm this match');
    http.expectNone('/api/metadata/tmdb/search');
    button('Verify chosen').click();
    const verify = http.expectOne('/api/metadata/tmdb/search');
    expect(verify.request.body).toMatchObject({
      id: 42,
      fileId: 'file-2',
      inputPath: 'TV/other.mkv',
      season: 2,
      episode: 4,
      language: 'fr',
    });
    verify.flush({ matches: [{ ...show, confirmationToken: 'new-file-token' }] });
  });

  it('remembers search state on dismissal but never treats it as a confirmation', async () => {
    const remembered = vi.fn<(state: TmdbPickerState) => void>();
    const confirmed = vi.fn();
    fixture.componentInstance.stateChanged.subscribe(remembered);
    fixture.componentInstance.confirmed.subscribe(confirmed);
    field('mediaType', 'tv', 'change');
    await fixture.whenStable();
    field('query', 'A Show');
    field('season', '3');
    field('episode', '7');
    await fixture.whenStable();
    button('Cancel matching').click();
    expect(remembered).toHaveBeenCalledWith({
      query: 'A Show',
      mediaType: 'tv',
      language: 'en-US',
      season: 3,
      episode: 7,
      matches: [],
      selected: null,
    });
    expect(confirmed).not.toHaveBeenCalled();
  });

  it('can search for a show before entering valid episode numbers', async () => {
    field('mediaType', 'tv', 'change');
    await fixture.whenStable();
    field('season', '');
    field('episode', '');
    await fixture.whenStable();
    expect(button('Search TMDB').disabled).toBe(false);
    button('Search TMDB').click();
    const search = http.expectOne('/api/metadata/tmdb/search');
    expect(search.request.body.season).toBeUndefined();
    expect(search.request.body.episode).toBeUndefined();
    search.flush({ matches: [show] });
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input[type="radio"]') as HTMLInputElement).click();
    await fixture.whenStable();
    expect(button('Verify chosen').disabled).toBe(true);
    field('season', '1000');
    field('episode', '1');
    await fixture.whenStable();
    expect(button('Verify chosen').disabled).toBe(true);
    field('season', '0');
    await fixture.whenStable();
    expect(button('Verify chosen').disabled).toBe(false);
  });

  function control(name: string): HTMLInputElement {
    return fixture.nativeElement.querySelector(`[formControlName="${name}"]`);
  }

  function field(name: string, value: string, event = 'input'): void {
    const input = control(name);
    input.value = value;
    input.dispatchEvent(new Event(event));
  }
  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }
});
