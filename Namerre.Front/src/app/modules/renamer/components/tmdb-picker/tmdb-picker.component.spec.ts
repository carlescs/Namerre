import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { tmdbOptionsHash } from '../../options-hash';
import type { MetadataSelection, TmdbPickerState } from '../../rename-api';
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
    expect(fixture.nativeElement.textContent).not.toContain('Verify');
    (fixture.nativeElement.querySelector('input[type="radio"]') as HTMLInputElement).click();
    await fixture.whenStable();
    // Choosing a candidate verifies it automatically for the entered season and episode.
    const first = http.expectOne('/api/metadata/tmdb/search');
    expect(first.request.body).toMatchObject({ id: 42, season: 2, episode: 3 });
    first.flush({ matches: [{ ...show, season: 2, episode: 3, confirmationToken: 'stale' }] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Use this match');
    // Editing the episode discards the verification; verifying again is a manual step.
    field('episode', '4');
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Use this match');
    http.expectNone('/api/metadata/tmdb/search');
    expect(button('Verify again').disabled).toBe(false);
    button('Verify again').click();
    const verify = http.expectOne('/api/metadata/tmdb/search');
    expect(verify.request.body).toMatchObject({
      id: 42,
      fileId: 'file-1',
      instanceId: 'tmdb-2',
      inputPath: 'TV/candidate.mkv',
      optionsHash: tmdbOptionsHash(plugin.options),
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
    button('Use this match').click();
    expect(confirmed).toHaveBeenCalledWith({
      fileId: 'file-1',
      instanceId: 'tmdb-2',
      inputPath: 'TV/candidate.mkv',
      optionsHash: tmdbOptionsHash(plugin.options),
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
    expect(fixture.nativeElement.textContent).not.toContain('Use this match');
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
    expect(fixture.nativeElement.textContent).not.toContain('Use this match');
    http.expectNone('/api/metadata/tmdb/search');
    button('Verify match').click();
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
    button('Cancel').click();
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
    http.expectNone('/api/metadata/tmdb/search');
    expect(button('Verify match').disabled).toBe(true);
    field('season', '1000');
    field('episode', '1');
    await fixture.whenStable();
    expect(button('Verify match').disabled).toBe(true);
    field('season', '0');
    await fixture.whenStable();
    expect(button('Verify match').disabled).toBe(false);
    button('Verify match').click();
    http.expectOne('/api/metadata/tmdb/search').flush({ matches: [] });
  });

  it('reports search failures and empty results', async () => {
    button('Search TMDB').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Looking up TMDB metadata…');
    http
      .expectOne('/api/metadata/tmdb/search')
      .flush({ title: 'TMDB rate limit reached.' }, { status: 429, statusText: 'Too Many' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'TMDB rate limit reached.',
    );
    expect(fixture.nativeElement.textContent).not.toContain('No matches.');
    button('Search TMDB').click();
    http.expectOne('/api/metadata/tmdb/search').flush({ matches: [] });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('No matches.');
  });

  it('does not search without query text', async () => {
    field('query', '');
    await fixture.whenStable();
    expect(button('Search TMDB').disabled).toBe(true);
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    http.expectNone('/api/metadata/tmdb/search');
  });

  it('verifies movies without episode numbers and shows unknown years', async () => {
    const movie = { ...show, id: 7, mediaType: 'movie', title: 'A Movie', year: null };
    button('Search TMDB').click();
    const search = http.expectOne('/api/metadata/tmdb/search');
    expect(search.request.body).toEqual({
      mediaType: 'movie',
      query: 'candidate',
      language: 'en-US',
    });
    search.flush({ matches: [movie] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('A Movie (Year unknown) — TMDB 7');
    (fixture.nativeElement.querySelector('input[type="radio"]') as HTMLInputElement).click();
    await fixture.whenStable();
    const verify = http.expectOne('/api/metadata/tmdb/search');
    expect(verify.request.body.id).toBe(7);
    expect(verify.request.body.season).toBeUndefined();
    expect(verify.request.body.episode).toBeUndefined();
    verify.flush({ matches: [{ ...movie, year: '1999', confirmationToken: 'token' }] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('A Movie (1999)');
    expect(fixture.nativeElement.textContent).not.toContain('Season');
  });

  it('refuses matches the server did not validate', async () => {
    const confirmed = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    await searchAndChoose();
    http
      .expectOne('/api/metadata/tmdb/search')
      .flush({ matches: [{ ...show, id: 99, confirmationToken: 'other' }, show] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('TMDB did not validate this match.');
    expect(fixture.nativeElement.textContent).not.toContain('Use this match');
    expect(confirmed).not.toHaveBeenCalled();
  });

  it('reports verification failures and allows retrying', async () => {
    await searchAndChoose();
    http
      .expectOne('/api/metadata/tmdb/search')
      .flush(
        { title: 'That TMDB movie does not exist.' },
        { status: 404, statusText: 'Not Found' },
      );
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('That TMDB movie does not exist.');
    expect(button('Verify again').disabled).toBe(false);
  });

  it('ignores a verification that completes after the picker was dismissed', async () => {
    const dismissed = vi.fn();
    fixture.componentInstance.dismissed.subscribe(dismissed);
    await searchAndChoose();
    const verify = http.expectOne('/api/metadata/tmdb/search');
    const dialog = fixture.nativeElement.querySelector('dialog') as HTMLDialogElement;
    dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(dismissed).toHaveBeenCalledTimes(1);
    expect(HTMLDialogElement.prototype.close).toHaveBeenCalled();
    expect(verify.cancelled).toBe(true);
  });

  it('remembers a verified match as the selected candidate', async () => {
    const remembered = vi.fn<(state: TmdbPickerState) => void>();
    fixture.componentInstance.stateChanged.subscribe(remembered);
    await searchAndChoose();
    const verified = { ...show, confirmationToken: 'token' };
    http.expectOne('/api/metadata/tmdb/search').flush({ matches: [verified] });
    await fixture.whenStable();
    button('Use this match').click();
    expect(remembered).toHaveBeenCalledWith(
      expect.objectContaining({ matches: [show], selected: verified }),
    );
  });

  it('shows a reused show hint and infers episode numbers from the file name', async () => {
    fixture.destroy();
    fixture = TestBed.createComponent(TmdbPickerComponent);
    fixture.componentRef.setInput('context', {
      fileId: 'file-3',
      plugin: { ...plugin, options: {} },
      inputPath: 'TV/A.Show.S03E09.mkv',
      reusedShow: true,
      state: {
        query: 'A Show',
        mediaType: 'tv',
        language: 'en-US',
        season: 3,
        episode: 9,
        matches: [],
        selected: null,
      },
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Reusing your previous show choice.');
    expect(control('season').value).toBe('3');
    expect(control('episode').value).toBe('9');
    expect(fixture.nativeElement.textContent).not.toContain('No matches.');
  });

  it('defaults the language when the plugin does not set one', async () => {
    fixture.destroy();
    fixture = TestBed.createComponent(TmdbPickerComponent);
    fixture.componentRef.setInput('context', {
      fileId: 'file-4',
      plugin: { ...plugin, options: {} },
      inputPath: 'Movies/Alien.1979.mkv',
    });
    await fixture.whenStable();
    expect(control('language').value).toBe('en-US');
    expect(control('query').value).toBe('Alien');
  });

  it('offers common locales while keeping the language free text', () => {
    const input = control('language');
    expect(input.getAttribute('list')).toBe('tmdb-locales');
    const options = Array.from(
      fixture.nativeElement.querySelectorAll('datalist#tmdb-locales option'),
    ).map((option) => (option as HTMLOptionElement).value);
    expect(options).toEqual(expect.arrayContaining(['en-US', 'ca-ES', 'pt-BR', 'zh-CN']));
    field('language', 'sv-SE');
    expect(control('language').value).toBe('sv-SE');
  });

  async function searchAndChoose(): Promise<void> {
    button('Search TMDB').click();
    http.expectOne('/api/metadata/tmdb/search').flush({ matches: [show] });
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input[type="radio"]') as HTMLInputElement).click();
    await fixture.whenStable();
  }

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
