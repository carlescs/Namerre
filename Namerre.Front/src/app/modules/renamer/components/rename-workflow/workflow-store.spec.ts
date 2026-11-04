import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { WorkflowMode } from '../../../../workflow-mode/workflow-mode';
import { loadPipelineDraft } from '../../draft-storage';
import { tmdbOptionsHash } from '../../options-hash';
import { casePlugin } from '../../plugin-catalog.testing';
import { RenameEvents } from '../../rename-events';
import { FakeRenameEvents } from '../../rename-events.testing';
import type { PlanView, PluginInstance, PreviewResult, TmdbSuggestion } from '../../rename-models';
import type { RenamerFile } from '../../renamer-file';
import { WorkflowStore } from './workflow-store';

describe('WorkflowStore', () => {
  let store: WorkflowStore;
  let mode: WorkflowMode;
  let events: FakeRenameEvents;
  let http: HttpTestingController;
  const file = (path: string): RenamerFile => ({
    name: path,
    path,
    folderPath: '',
    length: 1,
    lastModifiedUtc: '2026-10-18T00:00:00Z',
  });
  const upper: PluginInstance = {
    instanceId: 'case-1',
    typeId: 'case',
    version: 1,
    enabled: true,
    options: { mode: 'upper' },
  };
  const tmdb: PluginInstance = {
    instanceId: 'tmdb-1',
    typeId: 'tmdb',
    version: 1,
    enabled: true,
    options: { nameTemplate: '', language: 'en-US' },
  };
  const preview: PreviewResult = {
    files: [
      {
        id: 'a.txt',
        sourcePath: 'a.txt',
        targetPath: 'A.txt',
        unchanged: false,
        steps: [
          {
            instanceId: upper.instanceId,
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
  const missing = { code: 'MetadataRequired', message: 'Confirm a match.' };
  const tmdbPreview: PreviewResult = {
    files: [
      {
        id: 'a.mkv',
        sourcePath: 'a.mkv',
        targetPath: 'a.mkv',
        unchanged: true,
        steps: [
          {
            instanceId: tmdb.instanceId,
            typeId: 'tmdb',
            inputPath: 'a.mkv',
            outputPath: null,
            error: missing,
          },
        ],
        issues: [missing],
      },
    ],
    canPrepare: false,
  };
  const ready: PlanView = {
    id: 'plan-1',
    version: 1,
    status: 'Ready',
    completedFiles: 1,
    pipeline: { files: [{ id: 'a.txt', path: 'a.txt' }], plugins: [upper], selections: [] },
    files: [{ id: 'a.txt', sourcePath: 'a.txt', targetPath: 'A.txt', hash: 'hash', size: 1 }],
    issues: [],
    createdAt: '2026-10-18T00:00:00Z',
  };

  beforeEach(() => {
    localStorage.clear();
    events = new FakeRenameEvents();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RenameEvents, useValue: events },
        WorkflowStore,
      ],
    });
    mode = TestBed.inject(WorkflowMode);
    mode.stored.set('advanced');
    store = TestBed.inject(WorkflowStore);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  /** Advanced: files → plugins (catalog loaded) with the given steps, then on to the preview. */
  const toPlugins = (plugins: PluginInstance[], files = [file('a.txt')]) => {
    store.setCollection(files, true);
    store.next();
    http.expectOne('/api/rename-plugins').flush([casePlugin]);
    store.changePlugins(plugins);
  };

  const toPreview = () => {
    toPlugins([upper]);
    store.next();
    http.expectOne('/api/rename-previews').flush(preview);
  };

  const toReadyPlan = () => {
    toPreview();
    store.prepare();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
  };

  it('does not leave the files stage without a valid collection', () => {
    store.next();
    expect(store.stage()).toBe('files');
    store.setCollection([file('a.txt')], true);
    expect(store.validCollection()).toBe(true);
  });

  it('walks the Advanced stages and announces each move', () => {
    const entered = vi.fn();
    store.stageEntered.subscribe(entered);
    toPlugins([upper]);
    expect(store.stage()).toBe('plugins');
    expect(store.catalogLoaded()).toBe(true);
    expect(store.stages().map((stage) => stage.id)).toEqual([
      'files',
      'plugins',
      'preview',
      'confirmation',
    ]);
    store.next();
    const request = http.expectOne('/api/rename-previews');
    expect(request.request.body).toEqual(store.snapshot());
    request.flush(preview);
    expect(store.stage()).toBe('preview');
    expect(store.backwards()).toBe(false);
    expect(store.canPrepare()).toBe(true);
    store.back();
    expect(store.stage()).toBe('plugins');
    expect(store.backwards()).toBe(true);
    expect(entered).toHaveBeenCalledTimes(3);
  });

  it('adds the matching stage only while an enabled TMDB step exists', () => {
    toPlugins([upper, tmdb]);
    expect(store.stages().map((stage) => stage.id)).toContain('matching');
    store.changePlugins([upper, { ...tmdb, enabled: false }]);
    expect(store.stages().map((stage) => stage.id)).not.toContain('matching');
  });

  it('ignores a preview answered for steps that changed meanwhile', () => {
    toPlugins([upper]);
    store.next();
    const stale = http.expectOne('/api/rename-previews');
    vi.useFakeTimers();
    store.changePlugins([{ ...upper, options: { mode: 'lower' } }]);
    expect(stale.cancelled).toBe(true);
    expect(store.preview()).toBeNull();
    expect(store.canPrepare()).toBe(false);
    // The edit previews again after a short pause.
    vi.advanceTimersByTime(300);
    const fresh = http.expectOne('/api/rename-previews');
    expect(fresh.request.body.plugins[0].options.mode).toBe('lower');
    fresh.flush(preview);
    expect(store.preview()).toEqual(preview);
    expect(store.canPrepare()).toBe(true);
  });

  it('prepares, waits for the plan and executes it once', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    vi.useFakeTimers();
    toPreview();
    store.prepare();
    expect(store.stage()).toBe('confirmation');
    expect(store.preparing()).toBe(true);
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    http.expectOne('/api/rename-plans/plan-1').flush({ ...ready, status: 'Preparing' });
    expect(store.canConfirm()).toBe(false);
    events.push('Plan', 'plan-1');
    // Reloads are spaced out, so a burst of changes reads the plan once.
    vi.advanceTimersByTime(250);
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    expect(store.preparing()).toBe(false);
    expect(store.canConfirm()).toBe(true);
    store.confirm();
    const execute = http.expectOne('/api/rename-plans/plan-1/execute');
    expect(execute.request.body.version).toBe(1);
    expect(store.executing()).toBe(true);
    store.back();
    expect(store.stage()).toBe('confirmation');
    execute.flush({ id: 'run-1', status: 'Queued', location: '' });
    expect(navigate).toHaveBeenCalledWith(['/runs', 'run-1']);
  });

  it('keeps the plan when an edited name is rejected and prepares again when accepted', () => {
    toReadyPlan();
    store.overrideTarget('a.txt', 'taken.txt');
    expect(store.checkingOverride()).toBe(true);
    const rejected = http.expectOne('/api/rename-previews');
    expect(rejected.request.body.overrides).toEqual([{ fileId: 'a.txt', targetPath: 'taken.txt' }]);
    rejected.flush({
      ...preview,
      files: [{ ...preview.files[0], issues: [{ code: 'TargetExists', message: 'Taken.' }] }],
    });
    expect(store.error()).toBe('The new name cannot be used: Taken.');
    expect(store.plan()).toEqual(ready);
    expect(store.overrides()).toEqual([]);

    store.overrideTarget('a.txt', 'B.txt');
    http
      .expectOne('/api/rename-previews')
      .flush({ ...preview, files: [{ ...preview.files[0], targetPath: 'B.txt' }] });
    expect(store.overrides()).toEqual([{ fileId: 'a.txt', targetPath: 'B.txt' }]);
    const prepare = http.expectOne('/api/rename-plans');
    expect(prepare.request.body.overrides).toEqual([{ fileId: 'a.txt', targetPath: 'B.txt' }]);
    prepare.flush({ id: 'plan-2', status: 'Preparing', location: '' });
    http.expectOne('/api/rename-plans/plan-2').flush({ ...ready, id: 'plan-2' });
    expect(store.stage()).toBe('confirmation');
  });

  it('sends a new collection choice in the request and drops the overrides', () => {
    toReadyPlan();
    store.setNumberCollisions(true);
    expect(store.snapshot().onCollision).toBe('AddNumber');
    expect(store.plan()).toBeNull();
    expect(store.stage()).toBe('preview');
    store.refreshPreview();
    http.expectOne('/api/rename-previews').flush(preview);
  });

  it('suggests matches and accepts the confident ones together', () => {
    toPlugins([tmdb], [file('a.mkv')]);
    store.next();
    expect(store.stage()).toBe('matching');
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    expect(store.unmatchedFiles()).toBe(1);
    expect(store.canReview()).toBe(false);

    store.suggestMatches('movie');
    expect(store.suggesting()).toBe(true);
    const match = {
      id: 7,
      mediaType: 'movie',
      title: 'A',
      year: '2000',
      season: null,
      episode: null,
      episodeTitle: null,
      confirmationToken: 'token-a',
    };
    const suggestion: TmdbSuggestion = {
      parsed: { query: 'a', year: null, season: null, episode: null, episodeEnd: null },
      candidates: [match],
      match,
      confidence: 0.9,
      problem: null,
    };
    const request = http.expectOne('/api/metadata/tmdb/suggestions');
    expect(request.request.body).toEqual({
      fileId: 'a.mkv',
      instanceId: tmdb.instanceId,
      inputPath: 'a.mkv',
      optionsHash: tmdbOptionsHash(tmdb.options),
      mediaType: 'movie',
      language: 'en-US',
    });
    request.flush(suggestion);
    expect(store.suggesting()).toBe(false);
    expect(store.suggestProgress()).toEqual({ done: 1, total: 1 });
    expect(store.pendingMatches()).toHaveLength(1);

    vi.useFakeTimers();
    store.acceptAllMatches();
    expect(store.selections()).toEqual(store.snapshot().selections);
    expect(store.selections()[0].confirmationToken).toBe('token-a');
    vi.advanceTimersByTime(300);
    const next = http.expectOne('/api/rename-previews');
    expect(next.request.body.selections).toHaveLength(1);
    next.flush({ ...tmdbPreview, files: [{ ...tmdbPreview.files[0], issues: [] }] });
    expect(store.canReview()).toBe(true);
  });

  it('opens the picker from the file name and remembers a chosen show for the next file', () => {
    toPlugins([tmdb], [file('Show.S01E02.mkv'), file('Show.S01E03.mkv')]);
    store.next();
    const step = (path: string) => ({ ...tmdbPreview.files[0].steps[0], inputPath: path });
    http.expectOne('/api/rename-previews').flush({
      files: ['Show.S01E02.mkv', 'Show.S01E03.mkv'].map((path) => ({
        ...tmdbPreview.files[0],
        id: path,
        sourcePath: path,
        targetPath: path,
        steps: [step(path)],
      })),
      canPrepare: false,
    });
    store.match('Show.S01E02.mkv', step('Show.S01E02.mkv'));
    expect(store.matching()?.state).toMatchObject({
      mediaType: 'movie',
      language: 'en-US',
      matches: [],
      selected: null,
    });
    expect(store.matching()?.reusedShow).toBe(false);
    const show = {
      id: 9,
      mediaType: 'tv',
      title: 'The Show',
      year: '2010',
      season: 1,
      episode: 2,
      episodeTitle: 'Two',
      confirmationToken: 'token-2',
    };
    const state = store.matching()?.state;
    if (!state) throw new Error('The picker opened without a state.');
    store.rememberMatch({ ...state, mediaType: 'tv', selected: show, matches: [show] });
    store.closeMatch();
    expect(store.matching()).toBeNull();
    store.match('Show.S01E03.mkv', step('Show.S01E03.mkv'));
    expect(store.matching()?.reusedShow).toBe(true);
    expect(store.matching()?.state).toMatchObject({
      query: 'The Show',
      mediaType: 'tv',
      season: 1,
    });
  });

  it('moves the open stage to its counterpart when the mode changes', () => {
    toPreview();
    mode.stored.set('simple');
    TestBed.tick();
    expect(store.stage()).toBe('review');
    expect(store.screen()).toBe('preview');
    // Simple runs use its fixed run options.
    expect(store.verifyContent()).toBe(false);
    expect(store.deleteEmptyFolders()).toBe(true);
    mode.stored.set('advanced');
    TestBed.tick();
    expect(store.stage()).toBe('preview');
  });

  it('starts afresh when the steps are discarded for Simple', () => {
    toPreview();
    mode.discarded.update((count) => count + 1);
    TestBed.tick();
    expect(store.plugins()).toEqual([]);
    expect(store.preview()).toBeNull();
  });

  it('saves the steps and run options as the draft', () => {
    toPlugins([upper]);
    store.setVerifyContent(true);
    store.setDeleteEmptyFolders(false);
    TestBed.tick();
    const draft = loadPipelineDraft();
    expect(draft?.plugins).toEqual([upper]);
    expect(draft?.verifyContent).toBe(true);
    expect(draft?.deleteEmptyFolders).toBe(false);
  });
});
