import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import {
  ActivatedRoute,
  convertToParamMap,
  type ParamMap,
  provideRouter,
  Router,
} from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { Auth } from '../../../../auth/auth';
import type { RunView } from '../../rename-api';
import { RenameEvents } from '../../rename-events';
import { FakeRenameEvents } from '../../rename-events.testing';
import { RunHistoryComponent } from './run-history.component';

describe('RunHistoryComponent', () => {
  let fixture: ComponentFixture<RunHistoryComponent>;
  let http: HttpTestingController;
  let params: BehaviorSubject<ParamMap>;
  let events: FakeRenameEvents;
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
    events = new FakeRenameEvents();
    await TestBed.configureTestingModule({
      imports: [RunHistoryComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
        { provide: RenameEvents, useValue: events },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    // Deleting runs and watch folders are for administrators; one test covers the user's view.
    TestBed.inject(Auth).role.set('Admin');
    fixture = TestBed.createComponent(RunHistoryComponent);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 10));
  });

  afterEach(() => {
    // The watch folder panel loads on its own; its spec covers it.
    for (const request of http?.match('/api/watch-folders') ?? []) request.flush([]);
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
    button('Check undo for selected').click();
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
    button('Undo 1 file').click();
    const rollback = http.expectOne('/api/rename-runs/run-1/rollback');
    expect(rollback.request.body).toEqual({
      fileIds: ['a'],
      operationId,
      deleteEmptyFolders: false,
    });
    rollback.flush({ id: 'rollback-1', status: 'Queued', location: '' });
    expect(navigate).toHaveBeenCalledWith(['/runs', 'rollback-1']);
  });

  it('starts the undo folder cleanup from the rename choice and sends the changed choice', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, deleteEmptyFolders: true });
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Check undo for selected').click();
    http.expectOne('/api/rename-runs/run-1/rollback-preview').flush({
      files: [{ id: 'a', safe: true, currentPath: 'A.txt', destinationPath: 'a.txt', issues: [] }],
    });
    await fixture.whenStable();
    const review = fixture.nativeElement.querySelector(
      'section[aria-labelledby="rollback-heading"]',
    ) as HTMLElement;
    expect(review.querySelector('label')?.textContent).toContain('Delete folders left empty');
    const cleanup = review.querySelector('label input') as HTMLInputElement;
    expect(cleanup.checked).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('The rename had this on.');
    cleanup.click();
    await fixture.whenStable();
    button('Undo 1 file').click();
    const rollback = http.expectOne('/api/rename-runs/run-1/rollback');
    expect(rollback.request.body.deleteEmptyFolders).toBe(false);
    rollback.flush({ id: 'rollback-1', status: 'Queued', location: '' });
  });

  it('fills the rollback check progress from the polled per-file count', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (
      fixture.nativeElement.querySelector('thead input[type="checkbox"]') as HTMLInputElement
    ).click();
    await fixture.whenStable();
    button('Check undo for selected').click();
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
    button('Check undo for selected').click();
    await fixture.whenStable();
    const progress = fixture.nativeElement.querySelector('progress') as HTMLProgressElement;
    expect(progress.getAttribute('aria-label')).toBe('Selected files checked for undo');
    expect(progress.value).toBe(0);
    expect(progress.max).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Checked 0 / 1 selected file for undo');
    expect(fixture.nativeElement.textContent).not.toContain('Processing the requested check');
    http
      .expectOne('/api/rename-runs/run-1/rollback-preview')
      .flush({ code: 'Busy', message: 'Try again.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
    button('Check undo for selected').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).not.toBeNull();
    http.expectOne('/api/rename-runs/run-1/rollback-preview').flush({
      files: [{ id: 'a', safe: true, currentPath: 'A.txt', destinationPath: 'a.txt', issues: [] }],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('progress')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Undo review — 1 file can be undone');
  });

  it('clears the rollback check indicator when navigating away mid-check', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Check undo for selected').click();
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
    button('Check undo for selected').click();
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
    expect(fixture.nativeElement.textContent).toContain('Undo review');
    all.click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Undo review');
    expect(rows()).toEqual([false, false, false]);
    expect(all.checked).toBe(false);
  });

  it('drops selected files that are no longer eligible after a refresh', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement).click();
    await fixture.whenStable();
    events.push('Run', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 300));
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
    expect(fixture.nativeElement.textContent).toContain('0 files selected');
    expect(button('Check undo for selected').disabled).toBe(true);
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

  it('selects related files together with their video for undo', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({
      ...run,
      files: [...run.files, { ...run.files[1], id: 'c', sourcePath: 'a.srt', companionOf: 'a' }],
    });
    await fixture.whenStable();
    const checkboxes = Array.from(
      fixture.nativeElement.querySelectorAll('input.file-select') as NodeListOf<HTMLInputElement>,
    );
    expect(fixture.nativeElement.textContent).toContain('Related file, renamed with its video');
    checkboxes[0].click();
    await fixture.whenStable();
    expect(checkboxes.map((checkbox) => checkbox.checked)).toEqual([true, false, true]);
    checkboxes[2].click();
    await fixture.whenStable();
    expect(checkboxes.map((checkbox) => checkbox.checked)).toEqual([true, false, false]);
    checkboxes[0].click();
    await fixture.whenStable();
    expect(checkboxes.map((checkbox) => checkbox.checked)).toEqual([false, false, false]);
  });

  it('invalidates an approved rollback when the selection changes', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    const checkbox = fixture.nativeElement.querySelector('input.file-select') as HTMLInputElement;
    checkbox.click();
    await fixture.whenStable();
    button('Check undo for selected').click();
    const pending = http.expectOne('/api/rename-runs/run-1/rollback-preview');
    // A late DOM event still cannot preserve approval for a changed selection.
    checkbox.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(pending.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('Undo review');
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
    expect(fixture.nativeElement.textContent).toContain('(Interrupted)');
    button('Check recovery evidence').click();
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
    expect(button('Move files back to where they started').disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Multiple equivalent copies found.');
    http.expectNone('/api/rename-runs/run-1/recover');
    button('Check recovery evidence').click();
    http.expectOne('/api/rename-runs/run-1/recovery-preview').flush({
      runId: 'run-1',
      canRecover: true,
      action: 'RestoreOriginal',
      files: [{ id: 'a', locatedPath: '.stage/a', state: 'Staged', issues: [] }],
    });
    await fixture.whenStable();
    http.expectNone('/api/rename-runs/run-1/recover');
    button('Move files back to where they started').click();
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

  it('names the watch folder that started a run and shows the folders on the history page', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, origin: 'watch:Downloads' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Started by watch folder Downloads');
    params.next(convertToParamMap({}));
    const page = {
      items: [{ ...run, origin: 'watch:Downloads' }],
      page: 1,
      pageSize: 20,
      hasMore: false,
    };
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    http.expectOne('/api/watch-folders').flush([
      {
        name: 'Downloads',
        path: 'downloads',
        preset: 'TV',
        paused: false,
        lastScanAt: null,
        inFlight: 0,
        completed: 1,
        error: null,
        problems: [],
        tmdbAutoConfirm: null,
      },
    ]);
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Watch folders');
    expect(text).toContain('Started by watch folder Downloads');
  });

  it('shows users who started runs but hides administrator controls from users', async () => {
    TestBed.inject(Auth).role.set('User');
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    const page = {
      items: [{ ...run, createdBy: { id: 2, username: 'frank' } }],
      page: 1,
      pageSize: 20,
      hasMore: false,
    };
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Started by frank');
    expect(element.textContent).not.toContain('Watch folders');
    expect(element.querySelector('[aria-label="Bulk actions"]')).toBeNull();
    expect(element.querySelector('button[aria-label^="Delete"]')).toBeNull();
    http.expectNone('/api/watch-folders');
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
    expect(fixture.nativeElement.textContent).toContain('Nothing has been renamed yet.');
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
    expect(section.querySelector('a[href="/runs/rollback-1"]')?.textContent).toContain('Undo');
    expect(section.querySelector('a[href="/runs/rollback-2"]')).not.toBeNull();
    expect(section.textContent).toContain('Waiting');
  });

  it('omits the rollback runs section when none exist', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, rollbackRuns: [] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Undos of this rename');
  });

  it('offers the run file list as CSV and JSON downloads', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    const links = [...fixture.nativeElement.querySelectorAll('a[download]')] as HTMLAnchorElement[];
    expect(links.map((link) => [link.textContent?.trim(), link.getAttribute('href')])).toEqual([
      ['CSV', '/api/rename-runs/run-1/export?format=csv'],
      ['JSON', '/api/rename-runs/run-1/export?format=json'],
    ]);
  });

  it('loads the saved plan once when the snapshot is opened and retries after a failure', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, parentRunId: 'parent-1' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('a[href="/runs/parent-1"]')).not.toBeNull();
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Loading saved settings…');
    http
      .expectOne('/api/rename-plans/plan-1')
      .flush({ title: 'Plan storage is unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Plan storage is unavailable.');
    button('Try again').click();
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
    expect(text).toContain('Plan plan-1, version 2 · Executed');
    expect(text).toContain('Letter case');
    expect(text).toContain('Disabled');
    expect(text).toContain('abc123');
    expect(text).not.toContain('No rename steps were used.');
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
    expect(fixture.nativeElement.textContent).toContain('No rename steps were used.');
  });

  it('shows load failures and retries on request', async () => {
    http
      .expectOne('/api/rename-runs/run-1')
      .flush({ title: 'That run does not exist.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'That run does not exist.',
    );
    button('Try again').click();
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('That run does not exist.');
    expect(fixture.nativeElement.textContent).toContain('· 2 files');
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

  it('shows finishing up when every file is completed but the run is still running', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'Running' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Finishing up…');
    events.push('Run', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'Completed' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Finishing up…');
    expect(fixture.nativeElement.textContent).toContain('Completed');
  });

  it('re-reads runs on pushed changes and polls only while they are active', async () => {
    const moving = run.files.map((file) => ({ ...file, status: 'Staged' }));
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'Running', files: moving });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('In progress');
    expect(events.polling).toBe(true);
    events.push('Run', 'run-2');
    events.push('Plan', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectNone('/api/rename-runs/run-1');
    events.push('Run', 'run-1');
    events.push('Run', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('· 2 files');
    expect(events.polling).toBe(false);
    // A settled run still follows pushes: completing a rollback rewrites its file statuses.
    await new Promise((resolve) => setTimeout(resolve, 300));
    events.push('Run', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-runs/run-1').flush({
      ...run,
      files: run.files.map((file) => (file.id === 'a' ? { ...file, status: 'RolledBack' } : file)),
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Undone');
  });

  it('refreshes the history page when any run changes', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    const page = { items: [], page: 1, pageSize: 20, hasMore: false };
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    events.push('Run', 'run-9', 'Deleted');
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush({ ...page, items: [run] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Completed');
  });

  it('reports a failed rollback check and allows checking again', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Check undo for selected').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Checked 0 / 2 selected files for undo');
    http
      .expectOne('/api/rename-runs/run-1/rollback-preview')
      .flush({ title: 'Run changed.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Run changed.');
    expect(button('Check undo for selected').disabled).toBe(false);
  });

  it('reports a refused rollback without leaving the review', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
    await fixture.whenStable();
    button('Check undo for selected').click();
    http.expectOne('/api/rename-runs/run-1/rollback-preview').flush({
      files: [{ id: 'a', safe: true, currentPath: 'A.txt', destinationPath: 'a.txt', issues: [] }],
    });
    await fixture.whenStable();
    button('Undo 1 file').click();
    http
      .expectOne('/api/rename-runs/run-1/rollback')
      .flush({ title: 'Queue is full.' }, { status: 429, statusText: 'Too Many Requests' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Queue is full.');
    expect(fixture.nativeElement.textContent).toContain('Undo review — 1 file can be undone');
  });

  it('opens a recovery run that has a new id and reports recovery failures', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, status: 'RecoveryRequired' });
    await fixture.whenStable();
    button('Check recovery evidence').click();
    http
      .expectOne('/api/rename-runs/run-1/recovery-preview')
      .flush({ title: 'Evidence unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Evidence unavailable.');
    button('Check recovery evidence').click();
    http.expectOne('/api/rename-runs/run-1/recovery-preview').flush({
      runId: 'run-1',
      canRecover: true,
      action: 'RestoreOriginal',
      files: [{ id: 'a', locatedPath: null, state: 'Missing', issues: [] }],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Unknown or unclear');
    button('Move files back to where they started').click();
    http
      .expectOne('/api/rename-runs/run-1/recover')
      .flush({ title: 'Recovery refused.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Recovery refused.');
    button('Move files back to where they started').click();
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
    expect(fixture.nativeElement.textContent).toContain('its undo records and its saved settings');
    button('Confirm deletion').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Deleting…');
    http
      .expectOne({ method: 'DELETE', url: '/api/rename-runs/run-1' })
      .flush(null, { status: 204, statusText: 'No Content' });
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [{ ...run, id: 'other' }], page: 1, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Page 1');
  });

  it('deletes the selected settled runs together after an explicit confirmation', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    const busy = { ...run, id: 'run-3', status: 'Running' };
    const page = {
      items: [run, { ...run, id: 'run-2' }, busy],
      page: 1,
      pageSize: 20,
      hasMore: false,
    };
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    expect(runCheckbox(2).disabled).toBe(true);
    expect(button('Delete selected').disabled).toBe(true);

    selectAllRuns().click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('2 selected');
    runCheckbox(1).click();
    await fixture.whenStable();
    expect(selectAllRuns().indeterminate).toBe(true);
    runCheckbox(1).click();
    await fixture.whenStable();
    expect(selectAllRuns().checked).toBe(true);

    button('Delete selected').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Delete 2 records?');
    button('Cancel').click();
    await fixture.whenStable();
    http.expectNone('/api/rename-run-deletions');
    expect(fixture.nativeElement.textContent).toContain('2 selected');

    button('Delete selected').click();
    await fixture.whenStable();
    button('Confirm deletion of 2').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Deleting…');
    const request = http.expectOne('/api/rename-run-deletions');
    expect(request.request.method).toBe('POST');
    expect(new Set(request.request.body.runIds)).toEqual(new Set(['run-1', 'run-2']));
    request.flush(['run-1', 'run-2']);
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush({ ...page, items: [busy] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('0 selected');
    expect(fixture.nativeElement.textContent).not.toContain('Delete 2 records?');
  });

  it('keeps the selection and reports a refused bulk deletion', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [run], page: 1, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    runCheckbox(0).click();
    await fixture.whenStable();
    button('Delete selected').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Delete 1 record?');
    button('Confirm deletion of 1').click();
    http
      .expectOne('/api/rename-run-deletions')
      .flush(
        { title: 'Only completed or recovered runs can be deleted.', code: 'RunNotSettled' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Only completed or recovered runs');
    expect(runCheckbox(0).checked).toBe(true);
  });

  it('drops chosen runs that stop being deletable but keeps choices across pages', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    const page = { items: [run], page: 1, pageSize: 20, hasMore: true };
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    runCheckbox(0).click();
    await fixture.whenStable();
    events.push('Run', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 300));
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ ...page, items: [{ ...run, status: 'RecoveryRequired' }] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('0 selected');

    events.push('Run', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    runCheckbox(0).click();
    await fixture.whenStable();
    button('Next page').click();
    http
      .expectOne('/api/rename-runs?page=2&pageSize=20')
      .flush({ items: [{ ...run, id: 'run-2' }], page: 2, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('1 selected');
    expect(runCheckbox(0).checked).toBe(false);
    selectAllRuns().click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('2 selected');
    // Unticking this page's header leaves the choice from page 1 alone.
    selectAllRuns().click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('1 selected');

    button('Previous page').click();
    http.expectOne('/api/rename-runs?page=1&pageSize=20').flush(page);
    await fixture.whenStable();
    expect(runCheckbox(0).checked).toBe(true);
    button('Clear selection').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('0 selected');
  });

  it('offers to select deletable runs on every page once the whole page is selected', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [run], page: 1, pageSize: 20, hasMore: true });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('on every page');
    selectAllRuns().click();
    await fixture.whenStable();

    button('Select all deletable runs on every page').click();
    http
      .expectOne('/api/rename-run-deletions/candidates')
      .flush({ title: 'Unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('1 selected');

    button('Select all deletable runs on every page').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Selecting runs on every page…');
    http
      .expectOne('/api/rename-run-deletions/candidates')
      .flush({ ids: ['run-1', 'run-2', 'run-3'], hasMore: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('3 selected');
    expect(fixture.nativeElement.textContent).toContain('Every deletable run on every page');

    button('Delete selected').click();
    await fixture.whenStable();
    button('Confirm deletion of 3').click();
    const request = http.expectOne('/api/rename-run-deletions');
    expect(request.request.body).toEqual({ runIds: ['run-1', 'run-2', 'run-3'] });
    request.flush(['run-1', 'run-2', 'run-3']);
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [], page: 1, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Nothing has been renamed yet.');
  });

  it('says when more deletable runs exist than one deletion takes', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [run], page: 1, pageSize: 20, hasMore: true });
    await fixture.whenStable();
    selectAllRuns().click();
    await fixture.whenStable();
    button('Select all deletable runs on every page').click();
    http
      .expectOne('/api/rename-run-deletions/candidates')
      .flush({ ids: ['run-1', 'run-2'], hasMore: true });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('The newest 2 deletable runs are selected');
    runCheckbox(0).click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('The newest 2');
    expect(fixture.nativeElement.textContent).toContain('1 selected');
  });

  it('refuses to send more runs than one deletion takes', async () => {
    await showListPage(1, [run], true);
    selectAllRuns().click();
    await fixture.whenStable();
    button('Select all deletable runs on every page').click();
    const ids = Array.from({ length: 1000 }, (_, index) => `old-${index}`);
    http.expectOne('/api/rename-run-deletions/candidates').flush({ ids, hasMore: true });
    await fixture.whenStable();
    expect(button('Delete selected').disabled).toBe(false);
    runCheckbox(0).click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('1001 selected');
    expect(fixture.nativeElement.textContent).toContain('One deletion takes at most 1000 runs');
    expect(button('Delete selected').disabled).toBe(true);
    http.expectNone('/api/rename-run-deletions');
  });

  it('ignores an every-page answer once the user changes the selection', async () => {
    await showListPage(1, [run], true);
    selectAllRuns().click();
    await fixture.whenStable();
    button('Select all deletable runs on every page').click();
    const candidates = http.expectOne('/api/rename-run-deletions/candidates');
    await fixture.whenStable();
    expect(button('Delete selected').disabled).toBe(true);
    runCheckbox(0).click();
    await fixture.whenStable();
    expect(candidates.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('0 selected');
    expect(fixture.nativeElement.textContent).not.toContain('Selecting runs on every page');
  });

  it('closes the bulk confirmation when the selection is cleared', async () => {
    await showListPage(1, [run], false);
    runCheckbox(0).click();
    await fixture.whenStable();
    button('Delete selected').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Delete 1 record?');
    button('Clear selection').click();
    await fixture.whenStable();
    runCheckbox(0).click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Delete 1 record?');
    expect(button('Delete selected').disabled).toBe(false);
  });

  it('returns to the first page after deleting runs chosen on other pages', async () => {
    await showListPage(1, [run], true);
    runCheckbox(0).click();
    await fixture.whenStable();
    button('Next page').click();
    http.expectOne('/api/rename-runs?page=2&pageSize=20').flush({
      items: [
        { ...run, id: 'run-2' },
        { ...run, id: 'run-3' },
      ],
      page: 2,
      pageSize: 20,
      hasMore: false,
    });
    await fixture.whenStable();
    runCheckbox(0).click();
    await fixture.whenStable();
    button('Delete selected').click();
    await fixture.whenStable();
    button('Confirm deletion of 2').click();
    http.expectOne('/api/rename-run-deletions').flush(['run-1', 'run-2']);
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [{ ...run, id: 'run-3' }], page: 1, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Page 1');
  });

  it('describes rollback records without mentioning a prepared plan', async () => {
    http.expectOne('/api/rename-runs/run-1').flush({ ...run, kind: 'Rollback' });
    await fixture.whenStable();
    deleteButton().click();
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Delete this undo record?');
    expect(text).not.toContain('its saved settings');
  });

  it('titles list entries from their folder and nests an undo under its rename', async () => {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    const rename = {
      ...run,
      id: 'rename-1',
      files: ['a', 'b'].map((id) => ({
        ...run.files[0],
        id,
        sourcePath: `Shows/Dark/S01/${id}.mkv`,
        targetPath: `Shows/Dark/S01/${id}-new.mkv`,
      })),
    };
    const undo = {
      ...rename,
      id: 'undo-1',
      kind: 'Rollback',
      parentRunId: 'rename-1',
      status: 'Running',
      files: rename.files.map((file) => ({ ...file, status: 'Pending' })),
    };
    http
      .expectOne('/api/rename-runs?page=1&pageSize=20')
      .flush({ items: [undo, rename], page: 1, pageSize: 20, hasMore: false });
    await fixture.whenStable();
    const items = Array.from(fixture.nativeElement.querySelectorAll('ul > li')) as HTMLElement[];
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Shows/Dark/S01');
    expect(items[0].textContent).toContain('Rename');
    expect(items[0].textContent).toContain('2 files');
    expect(items[0].textContent).not.toContain('rename-1');
    expect(items[1].textContent).toContain('Undo');
    expect(items[1].textContent).toContain('In progress');
    expect(items[1].textContent).toContain('Undo of Shows/Dark/S01');
    expect(items[1].className).toContain('ml-6');
    expect(items[0].querySelector('.bg-emerald-100')).not.toBeNull();
    expect(items[1].querySelector('.bg-sky-100')).not.toBeNull();
  });

  it('offers Try again only after a load failure', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Try again');
  });

  it('hides the temporary location column unless a file is unfinished', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Temporary location');
    http.verify();
    events.push('Run', 'run-1');
    await new Promise((resolve) => setTimeout(resolve, 300));
    http.expectOne('/api/rename-runs/run-1').flush({
      ...run,
      files: [{ ...run.files[0], status: 'Failed', error: 'Disk full.', errorCode: 'Io' }],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Temporary location');
    expect(fixture.nativeElement.textContent).toContain('Disk full.');
    expect(fixture.nativeElement.querySelector('.bg-rose-100')).not.toBeNull();
  });

  it('does not show a focus ring on the heading focused in code', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('h1').className).toContain(
      'focus-visible:outline-none',
    );
  });

  function deleteButton(): HTMLButtonElement {
    const result = fixture.nativeElement.querySelector(
      'button[aria-label^="Delete"]',
    ) as HTMLButtonElement | null;
    if (!result) throw new Error('Delete button not found');
    return result;
  }

  async function showListPage(page: number, items: RunView[], hasMore: boolean): Promise<void> {
    params.next(convertToParamMap({}));
    http.expectOne('/api/rename-runs/run-1');
    http
      .expectOne(`/api/rename-runs?page=${page}&pageSize=20`)
      .flush({ items, page, pageSize: 20, hasMore });
    await fixture.whenStable();
  }

  function runCheckbox(index: number): HTMLInputElement {
    const result = fixture.nativeElement.querySelectorAll('ul > li input[type="checkbox"]')[
      index
    ] as HTMLInputElement | undefined;
    if (!result) throw new Error(`Run checkbox ${index} not found`);
    return result;
  }

  function selectAllRuns(): HTMLInputElement {
    return fixture.nativeElement.querySelector(
      '[aria-label="Bulk actions"] input[type="checkbox"]',
    ) as HTMLInputElement;
  }

  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }
});
