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
      'input.file-select',
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

  it('fills the rollback check progress from the polled per-file count', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (
      fixture.nativeElement.querySelector('thead input[type="checkbox"]') as HTMLInputElement
    ).click();
    await fixture.whenStable();
    button('Check selected-file').click();
    await fixture.whenStable();
    const preview = http.expectOne('/api/rename-runs/run-1/rollback-preview');
    const operation = preview.request.body.operationId;
    const progress = () => fixture.nativeElement.querySelector('progress') as HTMLProgressElement;
    expect(progress().value).toBe(0);
    expect(progress().max).toBe(2);
    await new Promise((resolve) => setTimeout(resolve, 300));
    http
      .expectOne(`/api/rename-runs/run-1/rollback-preview/${operation}`)
      .flush({ completedFiles: 1, totalFiles: 2 });
    await fixture.whenStable();
    expect(progress().value).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Checked 1 / 2 selected files');
    await new Promise((resolve) => setTimeout(resolve, 500));
    http
      .expectOne(`/api/rename-runs/run-1/rollback-preview/${operation}`)
      .flush({ completedFiles: 0, totalFiles: 0 });
    await fixture.whenStable();
    expect(progress().value).toBe(1);
    preview.flush({
      files: ['a', 'b'].map((id) => ({
        id,
        safe: true,
        currentPath: `${id.toUpperCase()}.txt`,
        destinationPath: `${id}.txt`,
        issues: [],
      })),
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
    await new Promise((resolve) => setTimeout(resolve, 600));
    http.expectNone(`/api/rename-runs/run-1/rollback-preview/${operation}`);
  });

  it('shows a progress indicator while the rollback check runs', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement).click();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
    button('Check selected-file').click();
    await fixture.whenStable();
    const progress = fixture.nativeElement.querySelector('progress') as HTMLProgressElement;
    expect(progress.getAttribute('aria-label')).toBe('Selected files checked for rollback');
    expect(progress.value).toBe(0);
    expect(progress.max).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Checked 0 / 1 selected file for rollback');
    expect(fixture.nativeElement.textContent).not.toContain('Processing the requested check');
    http
      .expectOne('/api/rename-runs/run-1/rollback-preview')
      .flush({ code: 'Busy', message: 'Try again.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
    button('Check selected-file').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).not.toBeNull();
    http.expectOne('/api/rename-runs/run-1/rollback-preview').flush({
      files: [{ id: 'a', safe: true, currentPath: 'A.txt', destinationPath: 'a.txt', issues: [] }],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Rollback review — 1 safe files');
  });

  it('clears the rollback check indicator when navigating away mid-check', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Check selected-file').click();
    await fixture.whenStable();
    const preview = http.expectOne('/api/rename-runs/run-1/rollback-preview');
    expect(fixture.nativeElement.querySelector('progress')).not.toBeNull();
    params.next(convertToParamMap({ id: 'run-2' }));
    await fixture.whenStable();
    expect(preview.cancelled).toBe(true);
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Checked 0 / 1 selected file');
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-runs/run-2').flush({ ...run, id: 'run-2' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
  });

  it('selects and clears every completed file from the header checkbox', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({
      ...run,
      files: [...run.files, { ...run.files[0], id: 'c', sourcePath: 'c.txt', status: 'Failed' }],
    });
    await fixture.whenStable();
    const all = fixture.nativeElement.querySelector(
      'thead input[type="checkbox"]',
    ) as HTMLInputElement;
    const rows = () =>
      Array.from(
        fixture.nativeElement.querySelectorAll('input.file-select') as NodeListOf<HTMLInputElement>,
      ).map((checkbox) => checkbox.checked);
    (fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement).click();
    await fixture.whenStable();
    expect(all.indeterminate).toBe(true);
    all.click();
    await fixture.whenStable();
    expect(rows()).toEqual([true, true, false]);
    expect(all.checked).toBe(true);
    expect(all.indeterminate).toBe(false);
    button('Check selected-file').click();
    const preview = http.expectOne('/api/rename-runs/run-1/rollback-preview');
    expect(preview.request.body.fileIds).toEqual(['a', 'b']);
    preview.flush({
      files: ['a', 'b'].map((id) => ({
        id,
        safe: true,
        currentPath: `${id.toUpperCase()}.txt`,
        destinationPath: `${id}.txt`,
        issues: [],
      })),
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Rollback review');
    all.click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Rollback review');
    expect(rows()).toEqual([false, false, false]);
    expect(all.checked).toBe(false);
  });

  it('drops selected files that are no longer eligible after a refresh', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Refresh').click();
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-runs/run-1').flush({
      ...run,
      files: [{ ...run.files[0], status: 'RolledBack' }, run.files[1]],
    });
    await fixture.whenStable();
    const all = fixture.nativeElement.querySelector(
      'thead input[type="checkbox"]',
    ) as HTMLInputElement;
    expect(all.indeterminate).toBe(false);
    expect(all.checked).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('0 files explicitly selected');
    expect(button('Check selected-file').disabled).toBe(true);
  });

  it('does not offer rollback selection for files of a rollback run', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, kind: 'Rollback' });
    await fixture.whenStable();
    const checkboxes = Array.from(
      fixture.nativeElement.querySelectorAll(
        'input[type="checkbox"]',
      ) as NodeListOf<HTMLInputElement>,
    );
    expect(checkboxes.length).toBe(3);
    expect(checkboxes.every((checkbox) => checkbox.disabled)).toBe(true);
  });

  it('invalidates an approved rollback when the selection changes', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    const checkbox = fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement;
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
    deleteButton().click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Files on disk are not touched');
    button('Cancel').click();
    await fixture.whenStable();
    http.expectNone({ method: 'DELETE' });
    deleteButton().click();
    await fixture.whenStable();
    button('Confirm deletion').click();
    const request = http.expectOne('/api/rename-runs/run-1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(navigate).toHaveBeenCalledWith(['/runs']);
  });

  it('disables deletion for runs awaiting recovery', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'RecoveryRequired' });
    await fixture.whenStable();
    expect(deleteButton().disabled).toBe(true);
    expect(deleteButton().title).toBe('Only completed or recovered runs can be deleted');
  });

  it('reports a refused deletion from the history list and refreshes after success', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    const page = { items: [run], page: 1, pageSize: 20, hasMore: false };
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    deleteButton().click();
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

  it('links an original run to the rollback runs created from it', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({
      ...run,
      rollbackRuns: [
        { id: 'rollback-1', kind: 'Rollback', status: 'Completed', createdAt: run.createdAt },
        { id: 'rollback-2', kind: 'Rollback', status: 'Queued', createdAt: run.createdAt },
      ],
    });
    await fixture.whenStable();
    const section = fixture.nativeElement.querySelector(
      'section[aria-labelledby="rollback-runs-heading"]',
    ) as HTMLElement;
    expect(section.querySelector('a[href="/runs/rollback-1"]')?.textContent).toContain(
      'Rollback rollback-1',
    );
    expect(section.querySelector('a[href="/runs/rollback-2"]')).not.toBeNull();
    expect(section.textContent).toContain('Queued');
  });

  it('omits the rollback runs section when none exist', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, rollbackRuns: [] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Rollback runs');
  });

  it('loads the saved plan once when the snapshot is opened and retries after a failure', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, parentRunId: 'parent-1' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('a[href="/runs/parent-1"]')).not.toBeNull();
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Loading saved plan…');
    http
      .expectOne('/api/rename-plans/plan-1')
      .flush({ title: 'Plan storage is unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Plan storage is unavailable.');
    button('Retry saved plan').click();
    http.expectOne('/api/rename-plans/plan-1').flush({
      id: 'plan-1',
      version: 2,
      status: 'Executed',
      completedFiles: 1,
      pipeline: {
        files: [],
        plugins: [
          {
            instanceId: 'upper',
            typeId: 'case',
            version: 1,
            enabled: false,
            options: { mode: 'upper' },
          },
        ],
        selections: [],
      },
      files: [{ id: 'a', sourcePath: 'a.txt', targetPath: 'A.txt', hash: 'abc123', size: 1 }],
      issues: [],
      createdAt: '2026-10-18T00:00:00Z',
    });
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Plan plan-1, version 2 — Executed');
    expect(text).toContain('case — instance upper');
    expect(text).toContain('Disabled');
    expect(text).toContain('abc123');
    expect(text).not.toContain('Empty pipeline.');
    details.dispatchEvent(new Event('toggle'));
    http.expectNone('/api/rename-plans/plan-1');
  });

  it('reports an empty saved pipeline', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    http.expectOne('/api/rename-plans/plan-1').flush({
      id: 'plan-1',
      version: 1,
      status: 'Ready',
      completedFiles: 0,
      pipeline: { files: [], plugins: [], selections: [] },
      files: [],
      issues: [],
      createdAt: '2026-10-18T00:00:00Z',
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Empty pipeline.');
  });

  it('shows load failures and retries on request', async () => {
    http
      .expectOne('/api/rename-runs/run-1')
      .flush({ title: 'That run does not exist.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'That run does not exist.',
    );
    button('Refresh / retry').click();
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('That run does not exist.');
    expect(fixture.nativeElement.textContent).toContain('Status: Completed.');
  });

  it('shows history load failures', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Cannot reach the server.');
  });

  it('keeps polling active runs until they settle', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'Running' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Status: Running.');
    await new Promise((resolve) => setTimeout(resolve, 1050));
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Status: Completed.');
    await new Promise((resolve) => setTimeout(resolve, 1050));
    http.expectNone('/api/rename-runs/run-1');
  });

  it('reports a failed rollback check and allows checking again', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Check selected-file').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain(
      'Checked 0 / 2 selected files for rollback',
    );
    http
      .expectOne('/api/rename-runs/run-1/rollback-preview')
      .flush({ title: 'Run changed.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Run changed.');
    expect(button('Check selected-file').disabled).toBe(false);
  });

  it('reports a refused rollback without leaving the review', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Check selected-file').click();
    http.expectOne('/api/rename-runs/run-1/rollback-preview').flush({
      files: [{ id: 'a', safe: true, currentPath: 'A.txt', destinationPath: 'a.txt', issues: [] }],
    });
    await fixture.whenStable();
    button('Confirm rollback of 1').click();
    http
      .expectOne('/api/rename-runs/run-1/rollback')
      .flush({ title: 'Queue is full.' }, { status: 429, statusText: 'Too Many Requests' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Queue is full.');
    expect(fixture.nativeElement.textContent).toContain('Rollback review — 1 safe files');
  });

  it('opens a recovery run that has a new id and reports recovery failures', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'RecoveryRequired' });
    await fixture.whenStable();
    button('Inspect recovery evidence').click();
    http
      .expectOne('/api/rename-runs/run-1/recovery-preview')
      .flush({ title: 'Evidence unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Evidence unavailable.');
    button('Inspect recovery evidence').click();
    http.expectOne('/api/rename-runs/run-1/recovery-preview').flush({
      runId: 'run-1',
      canRecover: true,
      action: 'RestoreOriginal',
      files: [{ id: 'a', locatedPath: null, state: 'Missing', issues: [] }],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Unknown / ambiguous');
    button('Confirm explicit restoration').click();
    http
      .expectOne('/api/rename-runs/run-1/recover')
      .flush({ title: 'Recovery refused.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Recovery refused.');
    button('Confirm explicit restoration').click();
    http
      .expectOne('/api/rename-runs/run-1/recover')
      .flush({ id: 'recovery-2', status: 'Queued', location: '' });
    expect(navigate).toHaveBeenCalledWith(['/runs', 'recovery-2']);
  });

  it('returns to the previous page after deleting its last saved run', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [{ ...run, id: 'other' }], page: 1, pageSize: 20, hasMore: true });
    await fixture.whenStable();
    button('Next page').click();
    http
      .expectOne('/api/rename-runs?page=2&pageSize=20')
      .flush({ items: [run], page: 2, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    deleteButton().click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain(
      'its rollback records and its prepared plan',
    );
    button('Confirm deletion').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Deleting saved run…');
    http
      .expectOne({ method: 'DELETE', url: '/api/rename-runs/run-1' })
      .flush(null, { status: 204, statusText: 'No Content' });
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [{ ...run, id: 'other' }], page: 1, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Page 1');
  });

  it('describes rollback records without mentioning a prepared plan', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, kind: 'Rollback' });
    await fixture.whenStable();
    deleteButton().click();
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Delete this rollback record?');
    expect(text).not.toContain('prepared plan');
  });

  function deleteButton(): HTMLButtonElement {
    const result = fixture.nativeElement.querySelector(
      'button[aria-label="Delete saved run run-1"]',
    ) as HTMLButtonElement | null;
    if (!result) throw new Error('Delete button not found');
    return result;
  }

  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }
});
