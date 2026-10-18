import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { optionsHash } from '../../options-hash';
import {
  MetadataSelection,
  PlanView,
  PluginInstance,
  PreviewResult,
  TmdbPickerState,
} from '../../rename-api';
import { RenamerFile } from '../../renamer-file';
import { PluginEditorComponent } from '../plugin-editor/plugin-editor.component';
import { RenameWorkflowComponent } from './rename-workflow.component';
import { TmdbPickerComponent } from '../tmdb-picker/tmdb-picker.component';

describe('RenameWorkflowComponent', () => {
  let fixture: ComponentFixture<RenameWorkflowComponent>;
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
    optionsHash: optionsHash(JSON.stringify(tmdb.options)),
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
    await TestBed.configureTestingModule({
      imports: [RenameWorkflowComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    HTMLDialogElement.prototype.showModal = vi.fn();
    HTMLDialogElement.prototype.close = vi.fn();
    fixture = TestBed.createComponent(RenameWorkflowComponent);
    fixture.componentRef.setInput('files', files);
    await fixture.whenStable();
    http
      .expectOne('/api/rename-plugins')
      .flush([{ typeId: 'case', version: 1, name: 'Case', defaultOptions: { mode: 'upper' } }]);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
    vi.restoreAllMocks();
  });

  it('sends an empty pipeline and never prepares unchanged files', async () => {
    button('Preview').click();
    const request = http.expectOne('/api/rename-previews');
    expect(request.request.body.plugins).toEqual([]);
    request.flush({
      ...preview,
      files: [{ ...preview.files[0], targetPath: 'a.txt', unchanged: true, steps: [] }],
    });
    await fixture.whenStable();
    expect(text()).toContain('Unchanged');
    expect(button('Prepare immutable').disabled).toBe(true);
  });

  it('cancels superseded previews and sends exact ordered independent plugin instances', async () => {
    button('Preview').click();
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
    expect(button('Prepare immutable').disabled).toBe(true);
    button('Preview').click();
    const current = http.expectOne('/api/rename-previews');
    expect(current.request.body.plugins).toEqual(plugins);
    current.flush(preview);
    await fixture.whenStable();
    expect(text()).toContain('Input: a.txt');
    expect(text()).toContain('Output: A.txt');
  });

  it('shows hash progress, confirms only a ready immutable version, and navigates to a durable run', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await showPreview();
    button('Prepare immutable').click();
    http
      .expectOne('/api/rename-plans')
      .flush({ id: 'plan-1', status: 'Preparing', location: '/api/rename-plans/plan-1' });
    await waitForPoll();
    http
      .expectOne('/api/rename-plans/plan-1')
      .flush({ ...ready, status: 'Preparing', completedFiles: 0, files: [] });
    await fixture.whenStable();
    expect(text()).toContain('hashing 0 / 1');
    expect(button('Confirm and execute').disabled).toBe(true);
    await new Promise((resolve) => setTimeout(resolve, 760));
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(text()).toContain('strong-hash');
    expect(button('Confirm and execute').disabled).toBe(false);
    button('Confirm and execute').click();
    const execute = http.expectOne('/api/rename-plans/plan-1/execute');
    expect(execute.request.body.version).toBe(1);
    expect(execute.request.body.operationId).toBeTruthy();
    expect(execute.request.body.targetPath).toBeUndefined();
    execute.flush({ id: 'run-1', status: 'Queued', location: '/api/rename-runs/run-1' });
    expect(navigate).toHaveBeenCalledWith(['/runs', 'run-1']);
  });

  it('invalidates ready approval immediately when collection changes and ignores canceled plan polling', async () => {
    await showPreview();
    button('Prepare immutable').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    const pending = http.expectOne('/api/rename-plans/plan-1');
    fixture.componentRef.setInput('files', [
      ...files,
      { ...files[0], path: 'b.txt', name: 'b.txt' },
    ]);
    await fixture.whenStable();
    expect(pending.cancelled).toBe(true);
    expect(text()).not.toContain('Immutable plan plan-1');
    expect(button('Prepare immutable').disabled).toBe(true);
    http.expectNone('/api/rename-plans/plan-1/execute');
  });

  it('invalidates an already ready plan on configuration edits and debounces the next preview', async () => {
    await showPreview();
    button('Prepare immutable').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(button('Confirm and execute').disabled).toBe(false);
    const editor = fixture.debugElement.query(By.directive(PluginEditorComponent))
      .componentInstance as PluginEditorComponent;
    editor.pluginsChanged.emit([
      { instanceId: 'new', typeId: 'case', version: 1, enabled: true, options: { mode: 'lower' } },
    ]);
    await fixture.whenStable();
    expect(text()).not.toContain('Immutable plan plan-1');
    expect(button('Prepare immutable').disabled).toBe(true);
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
    expect(button('Prepare immutable').disabled).toBe(true);
  });

  it('uses the same execution idempotency key after a failed connection and blocks duplicate clicks', async () => {
    await showPreview();
    button('Prepare immutable').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    button('Confirm and execute').click();
    const first = http.expectOne('/api/rename-plans/plan-1/execute');
    await fixture.whenStable();
    expect(button('Confirm and execute').disabled).toBe(true);
    const operation = first.request.body.operationId;
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(text()).toContain('Cannot reach the server');
    button('Confirm and execute').click();
    const retry = http.expectOne('/api/rename-plans/plan-1/execute');
    expect(retry.request.body.operationId).toBe(operation);
    retry.flush(
      { detail: 'The plan is no longer ready.' },
      { status: 409, statusText: 'Conflict' },
    );
  });

  it('automatically exposes matching outside the collapsed trace before a manual preview', async () => {
    changePlugins([tmdb]);
    await fixture.whenStable();
    expect(text()).toContain('TMDB matching');
    await new Promise((resolve) => setTimeout(resolve, 310));
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    await fixture.whenStable();
    const matchingStage = fixture.nativeElement.querySelector(
      '[aria-labelledby="matching-heading"]',
    ) as HTMLElement;
    expect(matchingStage.querySelector('button')?.textContent).toContain('Choose / change');
    expect(matchingStage.closest('details')).toBeNull();
    expect(text()).toContain('Match required.');
    expect(text()).not.toContain('MetadataRequired');
    expect(text()).not.toContain('Final target:');
    expect(button('Prepare immutable').disabled).toBe(true);
  });

  it('retains matches across downstream edits and collection additions but still invalidates plans', async () => {
    await confirmMetadata();
    button('Prepare immutable').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await waitForPoll();
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(button('Confirm and execute').disabled).toBe(false);
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
    expect(text()).not.toContain('Immutable plan plan-1');
    expect(button('Prepare immutable').disabled).toBe(true);
    button('Preview').click();
    const changed = http.expectOne('/api/rename-previews');
    expect(changed.request.body.selections).toEqual([selection]);
    changed.flush(matchedPreview);
    await fixture.whenStable();
    fixture.componentRef.setInput('files', [
      ...files,
      { ...files[0], path: 'b.txt', name: 'b.txt' },
    ]);
    await fixture.whenStable();
    button('Preview').click();
    const added = http.expectOne('/api/rename-previews');
    expect(added.request.body.selections).toEqual([selection]);
    added.flush(matchedPreview);
    await fixture.whenStable();
    fixture.componentRef.setInput('files', [{ ...files[0], path: 'b.txt', name: 'b.txt' }]);
    await fixture.whenStable();
    button('Preview').click();
    const removed = http.expectOne('/api/rename-previews');
    expect(removed.request.body.selections).toEqual([]);
    removed.flush({ files: [], canPrepare: false });
  });

  it('drops a match when its TMDB options change', async () => {
    await confirmMetadata();
    changePlugins([{ ...tmdb, options: { ...tmdb.options, nameTemplate: '{title}' } }]);
    await fixture.whenStable();
    button('Preview').click();
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
    button('Preview').click();
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
    expect(button('Prepare immutable').disabled).toBe(true);
    button('Preview').click();
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
    button('Preview').click();
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
    button('Choose / change').click();
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
    button('Preview').click();
    const current = http.expectOne('/api/rename-previews');
    expect(current.request.body.selections).toEqual([selection]);
    current.flush({ ...twoFiles, files: [matchedPreview.files[0], twoFiles.files[1]] });
    await fixture.whenStable();
    button('Choose / change').click();
    await fixture.whenStable();
    expect(picker().context().state).toEqual(state);
    expect(picker().context().reusedShow).toBe(false);
    button('Cancel matching').click();
    await fixture.whenStable();
    const choices = fixture.nativeElement.querySelectorAll(
      '[aria-labelledby="matching-heading"] button',
    ) as NodeListOf<HTMLButtonElement>;
    choices[1].click();
    await fixture.whenStable();
    expect(picker().context()).toMatchObject({
      fileId: 'b.txt',
      reusedShow: true,
      state: { ...state, episode: 1 },
    });
    expect(text()).not.toContain('Confirm this match');
    button('Cancel matching').click();
    await fixture.whenStable();
    button('Preview').click();
    const unconfirmed = http.expectOne('/api/rename-previews');
    expect(unconfirmed.request.body.selections).toEqual([selection]);
    unconfirmed.flush(twoFiles);
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
    button('Preview').click();
    http.expectOne('/api/rename-previews').flush(tmdbPreview);
    await fixture.whenStable();
    button('Choose / change').click();
    await fixture.whenStable();
    picker().confirmed.emit(selection);
    await fixture.whenStable();
    button('Preview').click();
    const confirmed = http.expectOne('/api/rename-previews');
    expect(confirmed.request.body.selections).toEqual([selection]);
    confirmed.flush(matchedPreview);
    await fixture.whenStable();
  }

  async function showPreview(): Promise<void> {
    button('Preview').click();
    http.expectOne('/api/rename-previews').flush(preview);
    await fixture.whenStable();
  }
  async function waitForPoll(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  function text(): string {
    return fixture.nativeElement.textContent;
  }
  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }
});
