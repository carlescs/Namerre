import { TestBed } from '@angular/core/testing';
import type { TmdbMatchRow } from '../../rename-api';
import { TmdbMatchingComponent } from './tmdb-matching.component';

describe('TmdbMatchingComponent', () => {
  it('emits matching decisions and disables them during a lookup without needing API providers', async () => {
    await TestBed.configureTestingModule({ imports: [TmdbMatchingComponent] }).compileComponents();
    const fixture = TestBed.createComponent(TmdbMatchingComponent);
    const row: TmdbMatchRow = {
      fileId: 'a.mkv',
      position: 1,
      plugin: { instanceId: 'tmdb', typeId: 'tmdb', version: 1, enabled: true, options: {} },
      step: {
        instanceId: 'tmdb',
        typeId: 'tmdb',
        inputPath: 'a.mkv',
        outputPath: null,
        error: { code: 'MetadataRequired', message: 'Confirm metadata.' },
      },
      suggestion: {
        fileId: 'a.mkv',
        instanceId: 'tmdb',
        inputPath: 'a.mkv',
        optionsHash: 'binding',
        error: '',
        match: {
          id: 42,
          mediaType: 'movie',
          title: 'Movie',
          year: '2000',
          season: null,
          episode: null,
          episodeTitle: null,
          confirmationToken: 'verified',
        },
      },
    };
    fixture.componentRef.setInput('rows', [row]);
    fixture.componentRef.setInput('acceptableMatches', 1);
    const search = vi.fn();
    const accepted = vi.fn();
    const all = vi.fn();
    const match = vi.fn();
    fixture.componentInstance.searchRequested.subscribe(search);
    fixture.componentInstance.suggestionAccepted.subscribe(accepted);
    fixture.componentInstance.allAccepted.subscribe(all);
    fixture.componentInstance.matchRequested.subscribe(match);
    await fixture.whenStable();
    const button = (prefix: string, label = ''): HTMLButtonElement => {
      const result = (
        Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
      ).find((entry) =>
        label
          ? entry.getAttribute('aria-label')?.startsWith(label)
          : entry.textContent?.trim().startsWith(prefix),
      );
      if (!result) throw new Error(`Missing button: ${prefix}`);
      return result;
    };
    button('Find episode').click();
    button('Accept', 'Accept match').click();
    button('Accept high-confidence').click();
    button('Change').click();
    expect(search).toHaveBeenCalledWith('tv');
    expect(accepted).toHaveBeenCalledWith(row.suggestion);
    expect(all).toHaveBeenCalledOnce();
    expect(match).toHaveBeenCalledWith({ fileId: row.fileId, step: row.step });
    fixture.componentRef.setInput('suggesting', true);
    await fixture.whenStable();
    expect(button('Find episode').disabled).toBe(true);
    expect(button('Accept high-confidence').disabled).toBe(true);
    expect(button('Change').disabled).toBe(true);
    fixture.destroy();
  });

  describe('compact rows', () => {
    const plugin = { instanceId: 'tmdb', typeId: 'tmdb', version: 1, enabled: true, options: {} };
    const row = (fileId: string, over: Partial<TmdbMatchRow> = {}): TmdbMatchRow => ({
      fileId,
      position: 1,
      plugin,
      step: {
        instanceId: 'tmdb',
        typeId: 'tmdb',
        inputPath: fileId,
        outputPath: null,
        error: { code: 'MetadataRequired', message: 'Confirm metadata.' },
      },
      suggestion: undefined,
      ...over,
    });
    const text = (fixture: { nativeElement: HTMLElement }) =>
      (fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ');

    it('shows file name, folder, status and match on one row and prefers episodes for SxxExx names', async () => {
      await TestBed.configureTestingModule({
        imports: [TmdbMatchingComponent],
      }).compileComponents();
      const fixture = TestBed.createComponent(TmdbMatchingComponent);
      const path = 'TV/Show/Show.S01E02.mkv';
      const confirmed = row(path, {
        step: {
          instanceId: 'tmdb',
          typeId: 'tmdb',
          inputPath: path,
          outputPath: null,
          error: null,
        },
        suggestion: {
          fileId: path,
          instanceId: 'tmdb',
          inputPath: path,
          optionsHash: 'h',
          error: '',
          match: {
            id: 1,
            mediaType: 'tv',
            title: 'Show',
            year: '2020',
            season: 1,
            episode: 2,
            episodeTitle: 'Pilot',
            confirmationToken: 't',
          },
        },
      });
      const failing = row('TV/Show/Show.S01E03.mkv', {
        step: {
          instanceId: 'tmdb',
          typeId: 'tmdb',
          inputPath: 'TV/Show/Show.S01E03.mkv',
          outputPath: null,
          error: { code: 'TargetExists', message: 'Target exists.' },
        },
      });
      fixture.componentRef.setInput('rows', [confirmed, failing]);
      await fixture.whenStable();
      const content = text(fixture);
      expect(content).toContain('Show.S01E02.mkv');
      expect(content).toContain('TV/Show');
      expect(content).toContain('Confirmed');
      expect(content).toContain('Show (2020) · S01E02 · Pilot');
      expect(content).toContain('Problem');
      expect(content).toContain('Target exists. (TargetExists)');
      expect(content).not.toContain('step 1');
      const find = (label: string) =>
        (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]).find(
          (entry) => entry.textContent?.trim() === label,
        )!;
      expect(find('Find episode matches').classList.contains('btn-primary')).toBe(true);
      expect(find('Find movie matches').classList.contains('btn-primary')).toBe(false);
      expect(find('Accept high-confidence matches').classList.contains('btn-primary')).toBe(false);
      fixture.componentRef.setInput('acceptableMatches', 2);
      await fixture.whenStable();
      expect(find('Accept high-confidence matches (2)').classList.contains('btn-primary')).toBe(
        true,
      );
      expect(find('Remove from collection')).toBeDefined();
      fixture.destroy();
    });

    it('marks low-confidence proposals for review and shows each confidence', async () => {
      await TestBed.configureTestingModule({
        imports: [TmdbMatchingComponent],
      }).compileComponents();
      const fixture = TestBed.createComponent(TmdbMatchingComponent);
      const proposal = (fileId: string, confidence: number): TmdbMatchRow =>
        row(fileId, {
          suggestion: {
            fileId,
            instanceId: 'tmdb',
            inputPath: fileId,
            optionsHash: 'h',
            error: '',
            confidence,
            match: {
              id: 1,
              mediaType: 'movie',
              title: 'Movie',
              year: '2000',
              season: null,
              episode: null,
              episodeTitle: null,
              confirmationToken: 't',
            },
          },
        });
      fixture.componentRef.setInput('rows', [proposal('a.mkv', 0.92), proposal('b.mkv', 0.6)]);
      await fixture.whenStable();
      const [high, low] = Array.from(
        fixture.nativeElement.querySelectorAll('article'),
      ) as HTMLElement[];
      expect(high.textContent).toContain('Proposed');
      expect(high.textContent).toContain('92% confidence from the file name.');
      expect(low.textContent).toContain('Check proposal');
      expect(low.textContent).toContain('60% confidence from the file name — check it');
      expect(low.querySelector('[aria-label^="Accept match"]')).not.toBeNull();
      fixture.destroy();
    });

    it('shows progress while matching and the step number for several TMDB steps', async () => {
      await TestBed.configureTestingModule({
        imports: [TmdbMatchingComponent],
      }).compileComponents();
      const fixture = TestBed.createComponent(TmdbMatchingComponent);
      fixture.componentRef.setInput('rows', [
        row('Movies/A.2000.mkv'),
        row('Movies/A.2000.mkv', { position: 2, plugin: { ...plugin, instanceId: 'tmdb-2' } }),
      ]);
      fixture.componentRef.setInput('suggesting', true);
      fixture.componentRef.setInput('suggestProgress', { done: 12, total: 44 });
      await fixture.whenStable();
      expect(text(fixture)).toContain('Matching 12 of 44 files…');
      const bar = fixture.nativeElement.querySelector('progress') as HTMLProgressElement;
      expect(bar.value).toBe(12);
      expect(bar.max).toBe(44);
      expect(text(fixture)).toContain('step 2');
      const movies = (
        Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
      ).find((entry) => entry.textContent?.trim() === 'Find movie matches');
      expect(movies?.classList.contains('btn-primary')).toBe(true);
      fixture.destroy();
    });
  });
});
