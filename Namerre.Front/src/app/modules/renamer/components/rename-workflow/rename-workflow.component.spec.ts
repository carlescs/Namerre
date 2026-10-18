import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { PlanView, PreviewResult } from '../../rename-api';
import { RenamerFile } from '../../renamer-file';
import { PluginEditorComponent } from '../plugin-editor/plugin-editor.component';
import { RenameWorkflowComponent } from './rename-workflow.component';

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

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RenameWorkflowComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
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
