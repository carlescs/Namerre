import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  awaitingMetadata,
  isMatchingIssue,
  isSettledRun,
  type PipelineRequest,
  type PreviewFile,
  RenameApi,
  type RunView,
  renameError,
} from './rename-api';

describe('RenameApi', () => {
  let api: RenameApi;
  let http: HttpTestingController;
  const pipeline: PipelineRequest = {
    files: [{ id: 'f1', path: 'a.txt' }],
    plugins: [],
    selections: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(RenameApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the plugin catalog', () => {
    api.catalog().subscribe();
    expect(http.expectOne('/api/rename-plugins').request.method).toBe('GET');
  });

  it('posts pipelines for preview and preparation', () => {
    api.preview(pipeline).subscribe();
    api.prepare(pipeline).subscribe();
    const preview = http.expectOne('/api/rename-previews');
    const prepare = http.expectOne('/api/rename-plans');
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toBe(pipeline);
    expect(prepare.request.method).toBe('POST');
    expect(prepare.request.body).toBe(pipeline);
  });

  it('encodes identifiers in resource paths', () => {
    api.plan('a/b c').subscribe();
    api.run('run?1').subscribe();
    api.deleteRun('run#2').subscribe();
    expect(http.expectOne('/api/rename-plans/a%2Fb%20c').request.method).toBe('GET');
    expect(http.expectOne('/api/rename-runs/run%3F1').request.method).toBe('GET');
    expect(http.expectOne('/api/rename-runs/run%232').request.method).toBe('DELETE');
  });

  it('reads the deletion candidates from every page', () => {
    api.deletableRuns().subscribe();
    expect(http.expectOne('/api/rename-run-deletions/candidates').request.method).toBe('GET');
  });

  it('deletes several runs in one request', () => {
    api.deleteRuns(['run-1', 'run-2']).subscribe();
    const request = http.expectOne('/api/rename-run-deletions');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ runIds: ['run-1', 'run-2'] });
  });

  it('executes a reviewed plan version with an idempotent operation id', () => {
    api.execute('plan-1', 3, 'op-1').subscribe();
    const request = http.expectOne('/api/rename-plans/plan-1/execute');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ version: 3, operationId: 'op-1' });
  });

  it('pages run history with a fixed page size', () => {
    api.runs(4).subscribe();
    const request = http.expectOne((r) => r.url === '/api/rename-runs');
    expect(request.request.params.get('page')).toBe('4');
    expect(request.request.params.get('pageSize')).toBe('20');
  });

  it('posts rollback and recovery requests to their run', () => {
    const selection = { fileIds: ['f1'], operationId: 'op' };
    api.rollbackPreview('run-1', selection).subscribe();
    api.rollback('run-1', selection).subscribe();
    api.recoveryPreview('run-1').subscribe();
    api.recover('run-1', 'RestoreOriginal', 'op-2').subscribe();
    expect(http.expectOne('/api/rename-runs/run-1/rollback-preview').request.body).toBe(selection);
    expect(http.expectOne('/api/rename-runs/run-1/rollback').request.body).toBe(selection);
    expect(http.expectOne('/api/rename-runs/run-1/recovery-preview').request.body).toEqual({});
    expect(http.expectOne('/api/rename-runs/run-1/recover').request.body).toEqual({
      action: 'RestoreOriginal',
      operationId: 'op-2',
    });
  });

  it('searches TMDB through the server', () => {
    const request = { mediaType: 'movie', query: 'Alien', language: 'en-US' };
    let result: unknown;
    api.tmdb(request).subscribe((value) => (result = value));
    const call = http.expectOne('/api/metadata/tmdb/search');
    expect(call.request.method).toBe('POST');
    expect(call.request.body).toBe(request);
    call.flush({ matches: [] });
    expect(result).toEqual({ matches: [] });
  });
});

describe('rename helpers', () => {
  it('recognises metadata issues that need matching', () => {
    expect(isMatchingIssue('MetadataRequired')).toBe(true);
    expect(isMatchingIssue('MetadataStale')).toBe(true);
    expect(isMatchingIssue('TargetExists')).toBe(false);
  });

  it('detects previews awaiting metadata', () => {
    const file = (codes: string[]): PreviewFile => ({
      id: 'f',
      sourcePath: 'a',
      targetPath: 'a',
      unchanged: true,
      steps: [],
      issues: codes.map((code) => ({ code, message: code })),
    });
    expect(awaitingMetadata(file([]))).toBe(false);
    expect(awaitingMetadata(file(['TargetExists']))).toBe(false);
    expect(awaitingMetadata(file(['TargetExists', 'MetadataStale']))).toBe(true);
  });

  it.each([
    ['Completed', true],
    ['Recovered', true],
    ['Queued', false],
    ['Running', false],
    ['RecoveryRequired', false],
    ['Recovering', false],
  ])('treats %s runs as settled: %s', (status, settled) => {
    expect(isSettledRun({ status } as RunView)).toBe(settled);
  });

  it('prefers problem detail, then title, then a status message', () => {
    const response = (status: number, error: unknown) =>
      new HttpErrorResponse({ status, error, url: '/api/x' });
    expect(renameError(response(400, { detail: 'Detail.', title: 'Title.' }))).toBe('Detail.');
    expect(renameError(response(409, { title: 'Title.' }))).toBe('Title.');
    expect(renameError(response(500, null))).toBe(
      'The request failed (500). Retry or check the server configuration.',
    );
    expect(renameError(response(502, 'gateway html'))).toBe(
      'The request failed (502). Retry or check the server configuration.',
    );
  });

  it('reports network failures and unknown errors distinctly', () => {
    expect(renameError(new HttpErrorResponse({ status: 0 }))).toBe(
      'Cannot reach the server. Check your connection and retry.',
    );
    expect(renameError(new Error('boom'))).toBe('The request could not be completed. Retry.');
    expect(renameError(undefined)).toBe('The request could not be completed. Retry.');
  });
});
