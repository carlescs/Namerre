import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  ActivatedRoute,
  ParamMap,
  Router,
  convertToParamMap,
  provideRouter,
} from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { RunView } from '../../rename-api';
import { RunHistoryComponent } from './run-history.component';

describe('RunHistoryComponent', () => {
  let fixture: ComponentFixture<RunHistoryComponent>;
  let http: HttpTestingController;
  let params: BehaviorSubject<ParamMap>;
  const run: RunView = {
    id: 'run-1',
    planId: 'plan-1',
    parentRunId: null,
    kind: 'Rename',
    status: 'Completed',
    files: ['a', 'b'].map((id) => ({
      id,
      sourcePath: `${id}.txt`,
      targetPath: `${id.toUpperCase()}.txt`,
      stagingPath: `.stage/${id}`,
      hash: `hash-${id}`,
      status: 'Completed',
      error: null,
    })),
    journal: [
      {
        sequence: 1,
        kind: 'Move',
        fileId: 'a',
        fromPath: 'a.txt',
        toPath: '.stage/a',
        status: 'Completed',
      },
    ],
    createdAt: '2026-10-18T00:00:00Z',
    error: null,
  };

  beforeEach(async () => {
    params = new BehaviorSubject(convertToParamMap({ id: 'run-1' }));
    await TestBed.configureTestingModule({
      imports: [RunHistoryComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunHistoryComponent);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 10));
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
  });

  it('loads a durable route and submits only explicitly reviewed safe selected files', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    const checkboxes = fixture.nativeElement.querySelectorAll(
      'input[type="checkbox"]',
    ) as NodeListOf<HTMLInputElement>;
    checkboxes[0].click();
    checkboxes[1].click();
    await fixture.whenStable();
    button('Check selected-file').click();
    const preview = http.expectOne('/api/rename-runs/run-1/rollback-preview');
    expect(preview.request.body.fileIds).toEqual(['a', 'b']);
    const operationId = preview.request.body.operationId;
    preview.flush({
      files: [
        { id: 'a', safe: true, currentPath: 'A.txt', destinationPath: 'a.txt', issues: [] },
        {
          id: 'b',
          safe: false,
          currentPath: 'B.txt',
          destinationPath: 'b.txt',
          issues: [{ code: 'DestinationExists', message: 'Original destination is occupied.' }],
        },
      ],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Blocked — left untouched');
    expect(fixture.nativeElement.textContent).toContain('Original destination is occupied.');
    button('Confirm rollback of 1').click();
    const rollback = http.expectOne('/api/rename-runs/run-1/rollback');
    expect(rollback.request.body).toEqual({ fileIds: ['a'], operationId });
    rollback.flush({ id: 'rollback-1', status: 'Queued', location: '' });
    expect(navigate).toHaveBeenCalledWith(['/runs', 'rollback-1']);
  });

  it('invalidates an approved rollback when the selection changes', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    const checkbox = fixture.nativeElement.querySelector(
      'input[type="checkbox"]',
    ) as HTMLInputElement;
    checkbox.click();
    await fixture.whenStable();
    button('Check selected-file').click();
    const pending = http.expectOne('/api/rename-runs/run-1/rollback-preview');
    // A late DOM event still cannot preserve approval for a changed selection.
    checkbox.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(pending.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('Rollback review');
    http.expectNone('/api/rename-runs/run-1/rollback');
  });

  it('exposes staged evidence and requires a separate explicit recovery confirmation', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({
      ...run,
      status: 'RecoveryRequired',
      files: [
        { ...run.files[0], status: 'Staged', errorCode: 'Interrupted', error: 'Move interrupted.' },
      ],
      error: 'Interrupted after staging.',
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('.stage/a');
    expect(fixture.nativeElement.textContent).toContain('Interrupted after staging.');
    expect(fixture.nativeElement.textContent).toContain('Interrupted:');
    button('Inspect recovery evidence').click();
    http.expectOne('/api/rename-runs/run-1/recovery-preview').flush({
      runId: 'run-1',
      canRecover: false,
      action: 'RestoreOriginal',
      files: [
        {
          id: 'a',
          locatedPath: null,
          state: 'Ambiguous',
          issues: [{ code: 'Ambiguous', message: 'Multiple equivalent copies found.' }],
        },
      ],
    });
    await fixture.whenStable();
    expect(button('Confirm explicit restoration').disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Multiple equivalent copies found.');
    http.expectNone('/api/rename-runs/run-1/recover');
    button('Inspect recovery evidence').click();
    http.expectOne('/api/rename-runs/run-1/recovery-preview').flush({
      runId: 'run-1',
      canRecover: true,
      action: 'RestoreOriginal',
      files: [{ id: 'a', locatedPath: '.stage/a', state: 'Staged', issues: [] }],
    });
    await fixture.whenStable();
    http.expectNone('/api/rename-runs/run-1/recover');
    button('Confirm explicit restoration').click();
    const recover = http.expectOne('/api/rename-runs/run-1/recover');
    expect(recover.request.body.action).toBe('RestoreOriginal');
    recover.flush({ id: 'run-1', status: 'Queued', location: '' });
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-runs/run-1').flush(run);
  });

  it('cancels obsolete route loads and paginates durable history without mixing pages', async () => {
    const obsolete = http.expectOne('/api/rename-runs/run-1');
    params.next(convertToParamMap({}));
    expect(obsolete.cancelled).toBe(true);
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush({
      items: [run],
      page: 1,
      pageSize: 20,
      hasMore: true,
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Rename history');
    expect(button('Previous page').disabled).toBe(true);
    button('Next page').click();
    http.expectOne('/api/rename-runs?page=2&pageSize=20').flush({
      items: [],
      page: 2,
      pageSize: 20,
      hasMore: false,
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Page 2');
    expect(button('Next page').disabled).toBe(true);
  });

  it('deletes a settled run only after an explicit inline confirmation', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    button('Delete saved run').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Files on disk are not touched');
    button('Cancel').click();
    await fixture.whenStable();
    http.expectNone({ method: 'DELETE' });
    button('Delete saved run').click();
    await fixture.whenStable();
    button('Confirm deletion').click();
    const request = http.expectOne('/api/rename-runs/run-1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(navigate).toHaveBeenCalledWith(['/runs']);
  });

  it('hides deletion for runs awaiting recovery', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'RecoveryRequired' });
    await fixture.whenStable();
    expect(() => button('Delete saved run')).toThrow();
  });

  it('reports a refused deletion from the history list and refreshes after success', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    const page = { items: [run], page: 1, pageSize: 20, hasMore: false };
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    button('Delete saved run').click();
    await fixture.whenStable();
    button('Confirm deletion').click();
    http
      .expectOne({ method: 'DELETE', url: '/api/rename-runs/run-1' })
      .flush(
        { title: 'Only completed or recovered runs can be deleted.', code: 'RunNotSettled' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Only completed or recovered runs');
    button('Confirm deletion').click();
    http
      .expectOne({ method: 'DELETE', url: '/api/rename-runs/run-1' })
      .flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush({ ...page, items: [] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No saved runs yet.');
  });

  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }
});
