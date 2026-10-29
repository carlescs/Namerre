import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { tmdbOptionsHash } from '../../options-hash';
import { casePlugin } from '../../plugin-catalog.testing';
import type {
  MetadataSelection,
  PlanView,
  PluginInstance,
  PreviewResult,
  TmdbPickerState,
} from '../../rename-api';
import { RenameEvents } from '../../rename-events';
import { FakeRenameEvents } from '../../rename-events.testing';
import type { RenamerFile } from '../../renamer-file';
import { PluginEditorComponent } from '../plugin-editor/plugin-editor.component';
import { TmdbPickerComponent } from '../tmdb-picker/tmdb-picker.component';
import { RenameWorkflowComponent } from './rename-workflow.component';

describe('RenameWorkflowComponent', () => {
  let fixture: ComponentFixture<RenameWorkflowComponent>;
  let events: FakeRenameEvents;
  let http: HttpTestingController;
  const files: RenamerFile[] = [
    {
      name: 'a.txt',
      path: 'a.txt',
      folderPath: '',
      length: 1,
      lastModifiedUtc: '2026-10-18T00:00:00Z',
    },
  ];
  const preview: PreviewResult = {
    files: [
      {
        id: 'a.txt',
        sourcePath: 'a.txt',
        targetPath: 'A.txt',
        unchanged: false,
        steps: [
          {
            instanceId: 'case-1',
            typeId: 'case',
            inputPath: 'a.txt',
            outputPath: 'A.txt',
            error: null,
          },
        ],
        issues: [],
      },
    ],
    canPrepare: true,
  };
  const ready: PlanView = {
    id: 'plan-1',
    version: 1,
    status: 'Ready',
    completedFiles: 1,
    pipeline: { files: [{ id: 'a.txt', path: 'a.txt' }], plugins: [], selections: [] },
    files: [
      { id: 'a.txt', sourcePath: 'a.txt', targetPath: 'A.txt', hash: 'strong-hash', size: 1 },
    ],
    issues: [],
    createdAt: '2026-10-18T00:00:00Z',
  };
  const tmdb: PluginInstance = {
    instanceId: 'tmdb-1',
    typeId: 'tmdb',
    version: 1,
    enabled: true,
    options: { nameTemplate: '', language: 'en-US' },
  };
  const selection: MetadataSelection = {
    fileId: 'a.txt',
    instanceId: tmdb.instanceId,
    inputPath: 'a.txt',
    optionsHash: tmdbOptionsHash(tmdb.options),
    confirmationToken: 'confirmed-file-a',
  };
  const missing = { code: 'MetadataRequired', message: 'Confirm a match.' };
  const tmdbPreview: PreviewResult = {
    files: [
      {
        ...preview.files[0],
        targetPath: 'a.txt',
        unchanged: true,
        steps: [
          {
            instanceId: tmdb.instanceId,
            typeId: 'tmdb',
            inputPath: 'a.txt',
            outputPath: null,
            error: missing,
          },
        ],
        issues: [missing],
      },
    ],
    canPrepare: false,
  };
  const matchedPreview: PreviewResult = {
    files: [
      {
        ...tmdbPreview.files[0],
        targetPath: 'Movie (2000).txt',
        unchanged: false,
        steps: [
          {
            ...tmdbPreview.files[0].steps[0],
            outputPath: 'Movie (2000).txt',
            error: null,
          },
        ],
        issues: [],
      },
    ],
    canPrepare: true,
  };

  beforeEach(async () => {
    localStorage.clear();
    events = new FakeRenameEvents();
    await TestBed.configureTestingModule({
      imports: [RenameWorkflowComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RenameEvents, useValue: events },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
    fixture = TestBed.createComponent(RenameWorkflowComponent);
    fixture.componentRef.setInput('files', files);
    await fixture.whenStable();
    button('Next: configure plugins').click();
    await fixture.whenStable();
    http
      .expectOne('/api/rename-plugins')
      .flush([{ ...casePlugin, defaultOptions: { mode: 'upper' } }]);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
    vi.restoreAllMocks();
  });

  it('skips metadata for non-TMDB plugins and preserves the editor when navigating back', async () => {
    const editor = fixture.debugElement.query(
      By.directive(PluginEditorComponent),
    ).componentInstance;
    button('Case').click();
    await fixture.whenStable();
    const mode = fixture.nativeElement.querySelector(
      'app-plugin-editor article select',
    ) as HTMLSelectElement;
    mode.value = 'lower';
    mode.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    button('Next: review filenames').click();
    const request = http.expectOne('/api/rename-previews');
    expect(request.request.body.plugins[0].options.mode).toBe('lower');
    request.flush(preview);
    await fixture.whenStable();
    expect(heading().textContent).toBe('Review filenames');
    expect(
      fixture.nativeElement.querySelector('.wizard-screens').classList.contains('backwards'),
    ).toBe(false);
    expect(document.activeElement).toBe(heading());
    expect(fixture.nativeElement.querySelector('[aria-current="step"]').textContent).toContain(
      'Review filenames',
    );
    expect(fixture.nativeElement.querySelector('[aria-labelledby="matching-heading"]')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('app-plugin-editor').closest('[hidden]'),
    ).not.toBeNull();
    button('Back').click();
    await fixture.whenStable();
    expect(heading().textContent).toBe('Configure plugins');
    expect(
      fixture.nativeElement.querySelector('.wizard-screens').classList.contains('backwards'),
    ).toBe(true);
    expect(fixture.debugElement.query(By.directive(PluginEditorComponent)).componentInstance).toBe(
      editor,
    );
    expect(mode.value).toBe('lower');
    button('Back').click();
    await fixture.whenStable();
    expect(heading().textContent).toBe('Select files');
    button('Next: configure plugins').click();
    await fixture.whenStable();
    expect(mode.value).toBe('lower');
    http.expectNone('/api/rename-plugins');
    http.expectNone('/api/rename-plans');
  });

  it('keeps preview separate from final confirmation and prevents navigating during preparation', async () => {
    await showPreview();
    expect(heading().textContent).toBe('Review filenames');
    http.expectNone('/api/rename-plans');
    button('Check files and continue').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    expect(heading().textContent).toBe('Confirm renaming');
    expect(button('Back').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('app-rename-preview').hidden).toBe(true);
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(button('Back').disabled).toBe(false);
    button('Back').click();
    await fixture.whenStable();
    expect(heading().textContent).toBe('Review filenames');
    expect(
      fixture.nativeElement.querySelector('[aria-labelledby="plan-heading"]').closest('[hidden]'),
    ).not.toBeNull();
    http.expectNone('/api/rename-plans/plan-1/execute');
  });

  it('sends an empty pipeline and never prepares unchanged files', async () => {
    button('Next: review filenames').click();
    const request = http.expectOne('/api/rename-previews');
    expect(request.request.body.plugins).toEqual([]);
    request.flush({
      ...preview,
      files: [{ ...preview.files[0], targetPath: 'a.txt', unchanged: true, steps: [] }],
    });
    await fixture.whenStable();
    expect(text()).toContain('Unchanged');
    expect(button('Check files and continue').disabled).toBe(true);
  });

  it('cancels superseded previews and sends exact ordered independent plugin instances', async () => {
    button('Next: review filenames').click();
    const stale = http.expectOne('/api/rename-previews');
    const editor = fixture.debugElement.query(By.directive(PluginEditorComponent))
      .componentInstance as PluginEditorComponent;
    const plugins = [
      {
        instanceId: 'second',
        typeId: 'case',
        version: 1,
        enabled: true,
        options: { mode: 'lower' },
      },
      {
        instanceId: 'first',
        typeId: 'case',
        version: 1,
        enabled: false,
        options: { mode: 'upper' },
      },
    ];
    editor.pluginsChanged.emit(plugins);
    await fixture.whenStable();
    expect(stale.cancelled).toBe(true);
    expect(button('Check files and continue').disabled).toBe(true);
    button('Refresh preview').click();
    const current = http.expectOne('/api/rename-previews');
    expect(current.request.body.plugins).toEqual(plugins);
    current.flush(preview);
    await fixture.whenStable();
    expect(text()).toContain('From a.txt');
    expect(text()).toContain('To A.txt');
    expect(text()).not.toContain('case-1');
  });

  it('shows check progress, confirms only a ready plan, and navigates to the saved run', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await showPreview();
    (fixture.nativeElement.querySelector('#quick-check') as HTMLInputElement).click();
    await fixture.whenStable();
    expect(button('Hash files and continue').textContent).toContain('Hash files');
    button('Hash files and continue').click();
    http
      .expectOne('/api/rename-plans')
      .flush({ id: 'plan-1', status: 'Preparing', location: '/api/rename-plans/plan-1' });
    await waitForPoll();
    http
      .expectOne('/api/rename-plans/plan-1')
      .flush({ ...ready, status: 'Preparing', completedFiles: 0, files: [] });
    await fixture.whenStable();
    expect(text()).toContain('Checking 0 of 1 files');
    expect(() => button('Rename 1 file')).toThrow();
    events.push('Plan', 'plan-1');
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(text()).toContain('strong-hash');
    expect(button('Rename 1 file').disabled).toBe(false);
    button('Rename 1 file').click();
    const execute = http.expectOne('/api/rename-plans/plan-1/execute');
    expect(execute.request.body.version).toBe(1);
    expect(execute.request.body.operationId).toBeTruthy();
    expect(execute.request.body.targetPath).toBeUndefined();
    localStorage.setItem('namerre.draft.files.v1', JSON.stringify(files));
    execute.flush({ id: 'run-1', status: 'Queued', location: '/api/rename-runs/run-1' });
    expect(navigate).toHaveBeenCalledWith(['/runs', 'run-1']);
    expect(localStorage.getItem('namerre.draft.files.v1')).toBeNull();
  });

  it('saves the rules and run options as a draft for the next visit', async () => {
    changePlugins([tmdb]);
    await fixture.whenStable();
    expect(JSON.parse(localStorage.getItem('namerre.draft.pipeline.v1') ?? 'null')).toEqual({
      plugins: [tmdb],
      verifyContent: false,
      deleteEmptyFolders: false,
    });
    for (const request of http.match('/api/rename-previews')) request.flush(tmdbPreview);
  });

  it('keeps emptied folders by default', async () => {
    await showPreview();
    button('Check files and continue').click();
    expect(http.expectOne('/api/rename-plans').request.body.deleteEmptyFolders).toBe(false);
  });

  it('prepares a plan that deletes emptied folders when requested', async () => {
    await showPreview();
    const folders = fixture.nativeElement.querySelector(
      '#delete-empty-folders',
    ) as HTMLInputElement;
    expect(folders.checked).toBe(false);
    folders.click();
    await fixture.whenStable();
    button('Check files and continue').click();
    expect(http.expectOne('/api/rename-plans').request.body.deleteEmptyFolders).toBe(true);
  });

  it('prepares a quick-check plan without content hashing by default', async () => {
    await showPreview();
    const quick = fixture.nativeElement.querySelector('#quick-check') as HTMLInputElement;
    expect(quick.checked).toBe(true);
    expect(button('Check files and continue').textContent).not.toContain('Hash files');
    button('Check files and continue').click();
    const prepare = http.expectOne('/api/rename-plans');
    expect(prepare.request.body.verifyContent).toBe(false);
    prepare.flush({ id: 'plan-1', status: 'Preparing', location: '/api/rename-plans/plan-1' });
    await waitForPoll();
    http
      .expectOne('/api/rename-plans/plan-1')
      .flush({ ...ready, status: 'Preparing', completedFiles: 0, files: [] });
    await fixture.whenStable();
    expect(text()).toContain('Checking 0 of 1 files');
  });

  it('invalidates ready approval immediately when collection changes and ignores canceled plan polling', async () => {
    await showPreview();
    button('Check files and continue').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    const pending = http.expectOne('/api/rename-plans/plan-1');
    fixture.componentRef.setInput('files', [
      ...files,
      { ...files[0], path: 'b.txt', name: 'b.txt' },
    ]);
    await fixture.whenStable();
    expect(pending.cancelled).toBe(true);
    expect(text()).not.toContain('Ready to rename');
    expect(button('Check files and continue').disabled).toBe(true);
    http.expectNone('/api/rename-plans/plan-1/execute');
  });

  it('invalidates an already ready plan on configuration edits and debounces the next preview', async () => {
    await showPreview();
    button('Check files and continue').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(button('Rename 1 file').disabled).toBe(false);
    const editor = fixture.debugElement.query(By.directive(PluginEditorComponent))
      .componentInstance as PluginEditorComponent;
    editor.pluginsChanged.emit([
      { instanceId: 'new', typeId: 'case', version: 1, enabled: true, options: { mode: 'lower' } },
    ]);
    await fixture.whenStable();
    expect(text()).not.toContain('Ready to rename');
    expect(button('Check files and continue').disabled).toBe(true);
    http.expectNone('/api/rename-previews');
    await new Promise((resolve) => setTimeout(resolve, 310));
    http.expectOne('/api/rename-previews').flush({
      ...preview,
      canPrepare: false,
      files: [
        {
          ...preview.files[0],
          issues: [{ code: 'TargetExists', message: 'Choose a different target.' }],
        },
      ],
    });
    await fixture.whenStable();
    expect(text()).toContain('Choose a different target.');
    expect(button('Check files and continue').disabled).toBe(true);
  });

  it('uses the same execution idempotency key after a failed connection and blocks duplicate clicks', async () => {
    await showPreview();
    button('Check files and continue').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    button('Rename 1 file').click();
    const first = http.expectOne('/api/rename-plans/plan-1/execute');
    await fixture.whenStable();
    expect(button('Rename 1 file').disabled).toBe(true);
    const operation = first.request.body.operationId;
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(text()).toContain('Cannot reach the server');
    button('Rename 1 file').click();
    const retry = http.expectOne('/api/rename-plans/plan-1/execute');
    expect(retry.request.body.operationId).toBe(operation);
    retry.flush(
      { detail: 'The plan is no longer ready.' },
      { status: 409, statusText: 'Conflict' },
    );
  });

  it('shows matching in its own stage and blocks review until metadata is accepted', async () => {
    changePlugins([tmdb]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    await fixture.whenStable();
    expect(text()).toContain('TMDB matching');
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    await fixture.whenStable();
    const matchingStage = fixture.nativeElement.querySelector(
      '[aria-labelledby="matching-heading"]',
    ) as HTMLElement;
    expect(matchingStage.textContent).toContain('Find match');
    expect(matchingStage.closest('[hidden]')).toBeNull();
    expect(matchingStage.closest('details')).toBeNull();
    expect(text()).toContain('Needs match');
    expect(text()).not.toContain('MetadataRequired');
    expect(text()).not.toContain('Movie (2000).txt');
    expect(button('Next: review filenames').disabled).toBe(true);
    expect(text()).toContain('1 file still needs a confirmed TMDB match');
    expect(text()).toContain('1 file needs a TMDB match');
  });

  it('asks the owner to drop a file that cannot be matched', async () => {
    changePlugins([tmdb]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    await fixture.whenStable();
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    await fixture.whenStable();
    const removed = vi.fn();
    fixture.componentInstance.fileRemoved.subscribe(removed);
    button('Remove from collection').click();
    expect(removed).toHaveBeenCalledWith('a.txt');
    const show = button('Show file');
    show.click();
    expect(document.activeElement).toBe(
      fixture.nativeElement.querySelector('article[data-unmatched]'),
    );
  });

  it('retains matches across downstream edits and collection additions but still invalidates plans', async () => {
    await confirmMetadata();
    button('Check files and continue').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(button('Rename 1 file').disabled).toBe(false);
    changePlugins([
      tmdb,
      {
        instanceId: 'case-2',
        typeId: 'case',
        version: 1,
        enabled: true,
        options: { mode: 'lower' },
      },
    ]);
    await fixture.whenStable();
    expect(text()).not.toContain('Ready to rename');
    expect(button('Check files and continue').disabled).toBe(true);
    button('Refresh preview').click();
    const changed = http.expectOne('/api/rename-previews');
    expect(changed.request.body.selections).toEqual([selection]);
    changed.flush(matchedPreview);
    await fixture.whenStable();
    fixture.componentRef.setInput('files', [
      ...files,
      { ...files[0], path: 'b.txt', name: 'b.txt' },
    ]);
    await fixture.whenStable();
    button('Refresh preview').click();
    const added = http.expectOne('/api/rename-previews');
    expect(added.request.body.selections).toEqual([selection]);
    added.flush(matchedPreview);
    await fixture.whenStable();
    fixture.componentRef.setInput('files', [{ ...files[0], path: 'b.txt', name: 'b.txt' }]);
    await fixture.whenStable();
    button('Refresh preview').click();
    const removed = http.expectOne('/api/rename-previews');
    expect(removed.request.body.selections).toEqual([]);
    removed.flush({ files: [], canPrepare: false });
  });

  it('retains accepted metadata across filename and folder template edits', async () => {
    await confirmMetadata();
    button('Back').click();
    await fixture.whenStable();
    expect(heading().textContent).toBe('Match TMDB metadata');
    button('Back').click();
    await fixture.whenStable();
    changePlugins([
      {
        ...tmdb,
        options: {
          ...tmdb.options,
          nameTemplate: '{title}',
          changeFolder: true,
          folderTemplate: '{year}',
        },
      },
    ]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    const changed = http.expectOne('/api/rename-previews');
    expect(changed.request.body.selections).toEqual([selection]);
    changed.flush(matchedPreview);
    await fixture.whenStable();
    expect(button('Next: review filenames').disabled).toBe(false);
    button('Next: review filenames').click();
    await fixture.whenStable();
    expect(text()).toContain('Movie (2000).txt');
    expect(text()).toContain('Confirmed');
    expect(button('Check files and continue').disabled).toBe(false);
    http.expectNone('/api/metadata/tmdb/search');
  });

  it('drops a match when its TMDB metadata language changes', async () => {
    await confirmMetadata();
    changePlugins([{ ...tmdb, options: { ...tmdb.options, language: 'fr' } }]);
    await fixture.whenStable();
    button('Refresh preview').click();
    const changed = http.expectOne('/api/rename-previews');
    expect(changed.request.body.selections).toEqual([]);
    changed.flush(tmdbPreview);
  });

  it('drops a match rejected for a changed step input and keeps preparation blocked', async () => {
    await confirmMetadata();
    changePlugins([
      {
        instanceId: 'upstream',
        typeId: 'case',
        version: 1,
        enabled: true,
        options: { mode: 'upper' },
      },
      tmdb,
    ]);
    await fixture.whenStable();
    button('Refresh preview').click();
    http.expectOne('/api/rename-previews').flush({
      ...tmdbPreview,
      files: [
        {
          ...tmdbPreview.files[0],
          steps: [{ ...tmdbPreview.files[0].steps[0], inputPath: 'A.txt' }],
        },
      ],
    });
    await fixture.whenStable();
    expect(button('Check files and continue').disabled).toBe(true);
    button('Refresh preview').click();
    const next = http.expectOne('/api/rename-previews');
    expect(next.request.body.selections).toEqual([]);
    next.flush(tmdbPreview);
  });

  it('restores per-file picker state and reuses a show without approving another episode', async () => {
    fixture.componentRef.setInput('files', [
      ...files,
      { ...files[0], path: 'b.txt', name: 'b.txt' },
    ]);
    changePlugins([tmdb]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    const twoFiles = {
      ...tmdbPreview,
      files: [
        tmdbPreview.files[0],
        {
          ...tmdbPreview.files[0],
          id: 'b.txt',
          sourcePath: 'b.txt',
          steps: [{ ...tmdbPreview.files[0].steps[0], inputPath: 'b.txt' }],
        },
      ],
    };
    http.expectOne('/api/rename-previews').flush(twoFiles);
    await fixture.whenStable();
    button('Find match').click();
    await fixture.whenStable();
    const show = {
      id: 42,
      mediaType: 'tv',
      title: 'A Show',
      year: '2000',
      season: null,
      episode: null,
      episodeTitle: null,
      confirmationToken: null,
    };
    const state: TmdbPickerState = {
      query: 'A Show',
      mediaType: 'tv',
      language: 'en-US',
      season: 2,
      episode: 7,
      matches: [show],
      selected: show,
    };
    picker().stateChanged.emit(state);
    picker().confirmed.emit(selection);
    await fixture.whenStable();
    button('Refresh preview').click();
    const current = http.expectOne('/api/rename-previews');
    expect(current.request.body.selections).toEqual([selection]);
    current.flush({ ...twoFiles, files: [matchedPreview.files[0], twoFiles.files[1]] });
    await fixture.whenStable();
    button('Find match').click();
    await fixture.whenStable();
    expect(picker().context().state).toEqual(state);
    expect(picker().context().reusedShow).toBe(false);
    button('Cancel').click();
    await fixture.whenStable();
    const choices = fixture.nativeElement.querySelectorAll(
      '[aria-labelledby="matching-heading"] article button',
    ) as NodeListOf<HTMLButtonElement>;
    choices[1].click();
    await fixture.whenStable();
    expect(picker().context()).toMatchObject({
      fileId: 'b.txt',
      reusedShow: true,
      state: { ...state, episode: 1 },
    });
    expect(text()).not.toContain('Use this match');
    button('Cancel').click();
    await fixture.whenStable();
    button('Refresh preview').click();
    const unconfirmed = http.expectOne('/api/rename-previews');
    expect(unconfirmed.request.body.selections).toEqual([selection]);
    unconfirmed.flush(twoFiles);
  });

  it('proposes a verified movie from its cleaned filename without approving it automatically', async () => {
    await showAutomaticFiles(['Movies/The.Movie.2024.1080p.mkv']);
    button('Find movie matches').click();
    const search = http.expectOne('/api/metadata/tmdb/search');
    expect(search.request.body).toEqual({
      mediaType: 'movie',
      query: 'The Movie',
      language: 'en-US',
    });
    const movie = {
      id: 42,
      mediaType: 'movie',
      title: 'The Movie',
      year: '2024',
      season: null,
      episode: null,
      episodeTitle: null,
      confirmationToken: null,
    };
    search.flush({ matches: [{ ...movie, id: 7, year: '1990' }, movie] });
    const verify = http.expectOne('/api/metadata/tmdb/search');
    expect(verify.request.body).toMatchObject({
      id: 42,
      fileId: 'Movies/The.Movie.2024.1080p.mkv',
      inputPath: 'Movies/The.Movie.2024.1080p.mkv',
      instanceId: tmdb.instanceId,
      optionsHash: selection.optionsHash,
    });
    verify.flush({ matches: [{ ...movie, confirmationToken: 'movie-proposal' }] });
    await fixture.whenStable();
    expect(text()).toContain('The Movie');
    expect(text()).toContain('2024');
    expect(text()).toContain('Proposed');
    expect(button('Next: review filenames').disabled).toBe(true);
    (
      fixture.nativeElement.querySelector('[aria-label^="Accept match"]') as HTMLButtonElement
    ).click();
    await fixture.whenStable();
    button('Refresh preview').click();
    const accepted = http.expectOne('/api/rename-previews');
    expect(accepted.request.body.selections).toEqual([
      {
        ...selection,
        fileId: 'Movies/The.Movie.2024.1080p.mkv',
        inputPath: 'Movies/The.Movie.2024.1080p.mkv',
        confirmationToken: 'movie-proposal',
      },
    ]);
    accepted.flush({ files: [], canPrepare: false });
  });

  it('makes one search and one verification per file for files that share a title', async () => {
    await showAutomaticFiles(['Show.S01E01.mkv', 'Show.S01E02.mkv', 'Show.S01E03.mkv']);
    const primary = (fixture.nativeElement as HTMLElement).querySelector('button.btn-primary');
    expect(button('Find episode matches').classList.contains('btn-primary')).toBe(true);
    expect(button('Find movie matches').classList.contains('btn-primary')).toBe(false);
    expect(primary).not.toBeNull();
    button('Find episode matches').click();
    await fixture.whenStable();
    expect(text()).toContain('Matching 0 of 3 files');
    const show = {
      id: 1,
      mediaType: 'tv',
      title: 'Show',
      year: '2020',
      season: null,
      episode: null,
      episodeTitle: null,
      confirmationToken: null,
    };
    http.expectOne('/api/metadata/tmdb/search').flush({ matches: [show] });
    for (const episode of [1, 2, 3]) {
      const verify = http.expectOne('/api/metadata/tmdb/search');
      expect(verify.request.body).toMatchObject({ id: 1, season: 1, episode });
      verify.flush({
        matches: [{ ...show, season: 1, episode, confirmationToken: `token-${episode}` }],
      });
      await fixture.whenStable();
      if (episode === 1) expect(text()).toContain('Matching 1 of 3 files');
    }
    await fixture.whenStable();
    expect(text()).not.toContain('Matching');
    expect(text()).toContain('Show (2020) · S01E02');
  });

  it('infers and verifies each episode separately and restores its proposed search for correction', async () => {
    await showAutomaticFiles(['A.Show.S02E03.mkv', 'A.Show.2x04.mkv']);
    button('Find episode matches').click();
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
    for (const episode of [3, 4]) {
      if (episode === 3) {
        // The show search is shared by both episodes; only verification repeats.
        const search = http.expectOne('/api/metadata/tmdb/search');
        expect(search.request.body).toMatchObject({ query: 'A Show', mediaType: 'tv' });
        search.flush({ matches: [show] });
      }
      const verify = http.expectOne('/api/metadata/tmdb/search');
      expect(verify.request.body).toMatchObject({ id: 42, season: 2, episode });
      verify.flush({
        matches: [
          {
            ...show,
            season: 2,
            episode,
            episodeTitle: `Episode ${episode}`,
            confirmationToken: `episode-${episode}`,
          },
        ],
      });
    }
    await fixture.whenStable();
    expect(text()).toContain('Episode 3');
    expect(text()).toContain('Episode 4');
    expect(button('Accept all matches').disabled).toBe(false);
    button('Change').click();
    await fixture.whenStable();
    expect(picker().context().state).toMatchObject({
      mediaType: 'tv',
      query: 'A Show',
      season: 2,
      episode: 3,
      selected: { id: 42 },
    });
    expect(text()).not.toContain('Use this match');
    button('Cancel').click();
    await fixture.whenStable();
    button('Accept all matches').click();
    await fixture.whenStable();
    button('Refresh preview').click();
    const accepted = http.expectOne('/api/rename-previews');
    expect(accepted.request.body.selections).toEqual([
      {
        ...selection,
        fileId: 'A.Show.S02E03.mkv',
        inputPath: 'A.Show.S02E03.mkv',
        confirmationToken: 'episode-3',
      },
      {
        ...selection,
        fileId: 'A.Show.2x04.mkv',
        inputPath: 'A.Show.2x04.mkv',
        confirmationToken: 'episode-4',
      },
    ]);
    accepted.flush({ files: [], canPrepare: false });
  });

  it('accepts all verified proposals in one preview and leaves failed matches unapproved', async () => {
    await showAutomaticFiles(['A.Movie.2024.mkv', 'Other.Movie.2023.mkv', 'Unknown.mkv']);
    expect(button('Accept all matches').disabled).toBe(true);
    button('Find movie matches').click();
    await fixture.whenStable();
    expect(button('Accept all matches').disabled).toBe(true);
    for (const [id, title, year] of [
      [42, 'A Movie', '2024'],
      [43, 'Other Movie', '2023'],
    ] as const) {
      const movie = {
        id,
        title,
        year,
        mediaType: 'movie',
        season: null,
        episode: null,
        episodeTitle: null,
        confirmationToken: null,
      };
      http.expectOne('/api/metadata/tmdb/search').flush({ matches: [movie] });
      http.expectOne('/api/metadata/tmdb/search').flush({
        matches: [{ ...movie, confirmationToken: `confirmed-${id}` }],
      });
    }
    http.expectOne('/api/metadata/tmdb/search').flush({ matches: [] });
    await fixture.whenStable();
    expect(button('Accept all matches').disabled).toBe(false);
    expect(text()).toContain('No TMDB matches');
    button('Accept all matches').click();
    await fixture.whenStable();
    expect(button('Accept all matches').disabled).toBe(true);
    button('Refresh preview').click();
    const accepted = http.expectOne('/api/rename-previews');
    expect(accepted.request.body.selections).toEqual([
      {
        ...selection,
        fileId: 'A.Movie.2024.mkv',
        inputPath: 'A.Movie.2024.mkv',
        confirmationToken: 'confirmed-42',
      },
      {
        ...selection,
        fileId: 'Other.Movie.2023.mkv',
        inputPath: 'Other.Movie.2023.mkv',
        confirmationToken: 'confirmed-43',
      },
    ]);
    accepted.flush({ files: [], canPrepare: false });
  });

  it('continues after a failed lookup and leaves unidentified episode numbers for manual correction', async () => {
    await showAutomaticFiles(['A.Show.S01E01.mkv', 'Unknown.Pilot.mkv', 'Other.Show.S01E02.mkv']);
    button('Find episode matches').click();
    http
      .expectOne('/api/metadata/tmdb/search')
      .flush(
        { detail: 'TMDB rate limit reached. Try later.' },
        { status: 429, statusText: 'Too Many Requests' },
      );
    const last = http.expectOne('/api/metadata/tmdb/search');
    expect(last.request.body.query).toBe('Other Show');
    last.flush({ matches: [] });
    await fixture.whenStable();
    expect(text()).toContain('TMDB rate limit reached');
    expect(text()).toContain('Could not infer the title or episode numbers');
    expect(text()).toContain('No TMDB matches');
    expect(button('Find episode matches').disabled).toBe(false);
    expect(button('Next: review filenames').disabled).toBe(true);
  });

  it('cancels suggestions on configuration changes and discards stale proposal information', async () => {
    await showAutomaticFiles(['A.Movie.2024.mkv']);
    button('Find movie matches').click();
    const stale = http.expectOne('/api/metadata/tmdb/search');
    changePlugins([{ ...tmdb, options: { ...tmdb.options, language: 'fr' } }]);
    await fixture.whenStable();
    expect(stale.cancelled).toBe(true);
    expect(text()).not.toContain('Proposed');
    button('Refresh preview').click();
    http.expectOne('/api/rename-previews').flush({ files: [], canPrepare: false });
  });

  it('evaluates step inputs before starting automatic lookups', async () => {
    changePlugins([tmdb]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    http.expectNone('/api/metadata/tmdb/search');
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    await fixture.whenStable();
    button('Find movie matches').click();
    http.expectOne('/api/metadata/tmdb/search').flush({ matches: [] });
    await fixture.whenStable();
    expect(text()).toContain('No TMDB matches');
  });

  async function showAutomaticFiles(paths: string[]): Promise<void> {
    fixture.componentRef.setInput(
      'files',
      paths.map((path) => ({
        ...files[0],
        path,
        name: path.split('/').pop() ?? '',
      })),
    );
    changePlugins([tmdb]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    http.expectOne('/api/rename-previews').flush({
      files: paths.map((path) => ({
        ...tmdbPreview.files[0],
        id: path,
        sourcePath: path,
        targetPath: path,
        steps: [{ ...tmdbPreview.files[0].steps[0], inputPath: path }],
      })),
      canPrepare: false,
    });
    await fixture.whenStable();
  }

  it('reports a catalog failure and retries on request', async () => {
    fixture.destroy();
    fixture = TestBed.createComponent(RenameWorkflowComponent);
    fixture.componentRef.setInput('files', files);
    await fixture.whenStable();
    button('Next: configure plugins').click();
    await fixture.whenStable();
    http
      .expectOne('/api/rename-plugins')
      .flush({ title: 'Catalog unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(text()).toContain('Catalog unavailable.');
    expect(button('Next: review filenames').disabled).toBe(true);
    button('Retry plugin catalog').click();
    http.expectOne('/api/rename-plugins').flush([]);
    await fixture.whenStable();
    expect(text()).not.toContain('Catalog unavailable.');
    expect(button('Next: review filenames').disabled).toBe(false);
  });

  it('reports preview failures and keeps preparation blocked', async () => {
    button('Next: review filenames').click();
    http
      .expectOne('/api/rename-previews')
      .flush({ title: 'Supply 1–200 files.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Supply 1–200 files.',
    );
    expect(button('Check files and continue').disabled).toBe(true);
    button('Refresh preview').click();
    http.expectOne('/api/rename-previews').flush(preview);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(button('Check files and continue').disabled).toBe(false);
  });

  it('reports a refused preparation and lets the user go back', async () => {
    await showPreview();
    button('Check files and continue').click();
    http
      .expectOne('/api/rename-plans')
      .flush({ title: 'Too many queued jobs.' }, { status: 429, statusText: 'Too Many' });
    await fixture.whenStable();
    expect(text()).toContain('Too many queued jobs.');
    expect(heading().textContent).toBe('Confirm renaming');
    expect(button('Back').disabled).toBe(false);
    expect(() => button('Check again')).toThrow();
    http.expectNone('/api/rename-plans/plan-1');
  });

  it('retries polling the preparation status after a failure', async () => {
    await showPreview();
    button('Check files and continue').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(text()).toContain('Cannot reach the server');
    expect(() => button('Rename 1 file')).toThrow();
    button('Check again').click();
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(text()).not.toContain('Cannot reach the server');
    expect(button('Rename 1 file').disabled).toBe(false);
  });

  it('returns from matching to plugin configuration when the TMDB step is removed', async () => {
    changePlugins([tmdb]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    await fixture.whenStable();
    expect(heading().textContent).toBe('Match TMDB metadata');
    changePlugins([]);
    await fixture.whenStable();
    expect(heading().textContent).toBe('Configure plugins');
    expect(fixture.nativeElement.querySelector('app-tmdb-matching')).toBeNull();
  });

  function changePlugins(plugins: PluginInstance[]): void {
    const editor = fixture.debugElement.query(By.directive(PluginEditorComponent))
      .componentInstance as PluginEditorComponent;
    editor.pluginsChanged.emit(plugins);
  }

  function picker(): TmdbPickerComponent {
    return fixture.debugElement.query(By.directive(TmdbPickerComponent))
      .componentInstance as TmdbPickerComponent;
  }

  async function confirmMetadata(): Promise<void> {
    changePlugins([tmdb]);
    await fixture.whenStable();
    button('Next: match metadata').click();
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    await fixture.whenStable();
    button('Find match').click();
    await fixture.whenStable();
    picker().confirmed.emit(selection);
    await fixture.whenStable();
    button('Refresh preview').click();
    const confirmed = http.expectOne('/api/rename-previews');
    expect(confirmed.request.body.selections).toEqual([selection]);
    confirmed.flush(matchedPreview);
    await fixture.whenStable();
    button('Next: review filenames').click();
    await fixture.whenStable();
  }

  async function showPreview(): Promise<void> {
    button('Next: review filenames').click();
    http.expectOne('/api/rename-previews').flush(preview);
    await fixture.whenStable();
  }
  async function waitForPoll(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  function text(): string {
    return fixture.nativeElement.textContent;
  }

  function heading(): HTMLHeadingElement {
    return fixture.nativeElement.querySelector('.stage-header h2');
  }
  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }
});
