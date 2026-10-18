import { TestBed } from '@angular/core/testing';
import { TmdbMatchRow } from '../../rename-api';
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
    fixture.componentRef.setInput('canAcceptAll', true);
    const search = vi.fn();
    const accepted = vi.fn();
    const all = vi.fn();
    const match = vi.fn();
    fixture.componentInstance.searchRequested.subscribe(search);
    fixture.componentInstance.suggestionAccepted.subscribe(accepted);
    fixture.componentInstance.allAccepted.subscribe(all);
    fixture.componentInstance.matchRequested.subscribe(match);
    await fixture.whenStable();
    const button = (prefix: string): HTMLButtonElement => {
      const result = (
        Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
      ).find((entry) => entry.textContent?.trim().startsWith(prefix));
      if (!result) throw new Error(`Missing button: ${prefix}`);
      return result;
    };
    button('Match episodes').click();
    button('Accept proposed').click();
    button('Accept all').click();
    button('Choose / change').click();
    expect(search).toHaveBeenCalledWith('tv');
    expect(accepted).toHaveBeenCalledWith(row.suggestion);
    expect(all).toHaveBeenCalledOnce();
    expect(match).toHaveBeenCalledWith({ fileId: row.fileId, step: row.step });
    fixture.componentRef.setInput('suggesting', true);
    await fixture.whenStable();
    expect(button('Match episodes').disabled).toBe(true);
    expect(button('Accept all').disabled).toBe(true);
    expect(button('Choose / change').disabled).toBe(true);
    fixture.destroy();
  });
});
