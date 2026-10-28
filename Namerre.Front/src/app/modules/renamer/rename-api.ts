import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface PluginOptions {
  search?: string;
  replacement?: string;
  ignoreCase?: boolean;
  scope?: string;
  pattern?: string;
  mode?: string;
  nameTemplate?: string;
  changeFolder?: boolean;
  baseFolder?: string;
  folderTemplate?: string;
  seasonFolder?: boolean;
  seasonFolderTemplate?: string;
  language?: string;
}
export interface PluginInstance {
  instanceId: string;
  typeId: string;
  version: number;
  enabled: boolean;
  options: PluginOptions;
}
export interface PluginDescriptor {
  typeId: string;
  version: number;
  name: string;
  defaultOptions: PluginOptions;
}
export interface MetadataSelection {
  fileId: string;
  instanceId: string;
  inputPath: string;
  optionsHash: string;
  confirmationToken: string;
}
export interface PipelineRequest {
  files: { id: string; path: string }[];
  plugins: PluginInstance[];
  selections: MetadataSelection[];
  /** False records size and modification time instead of hashing file contents (faster, weaker). */
  verifyContent?: boolean;
  /** True removes source folders that the run leaves empty once every file is placed. */
  deleteEmptyFolders?: boolean;
}
export interface RenameIssue {
  code: string;
  message: string;
}
export interface StepTrace {
  instanceId: string;
  typeId: string;
  inputPath: string;
  outputPath: string | null;
  error: RenameIssue | null;
}
export interface PreviewFile {
  id: string;
  sourcePath: string;
  targetPath: string;
  unchanged: boolean;
  steps: StepTrace[];
  issues: RenameIssue[];
}
export interface PreviewResult {
  files: PreviewFile[];
  canPrepare: boolean;
}
export interface JobAccepted {
  id: string;
  status: string;
  location: string;
}
export interface PlanView {
  id: string;
  version: number;
  status: string;
  completedFiles: number;
  pipeline: PipelineRequest;
  files: { id: string; sourcePath: string; targetPath: string; hash: string; size: number }[];
  issues: RenameIssue[];
  createdAt: string;
}
export interface RunFile {
  id: string;
  sourcePath: string;
  targetPath: string;
  stagingPath: string;
  hash: string;
  status: string;
  error: string | null;
  errorCode?: string | null;
}
export interface RunView {
  id: string;
  planId: string;
  parentRunId: string | null;
  kind: string;
  status: string;
  files: RunFile[];
  journal: {
    sequence: number;
    kind: string;
    fileId: string;
    fromPath: string;
    toPath: string;
    status: string;
  }[];
  createdAt: string;
  error: string | null;
  /** Snapshot of the plan option (or the undo's own choice); rollbacks start from it. */
  deleteEmptyFolders?: boolean;
  /** Rollback runs created from this run; only present on single-run reads. */
  rollbackRuns?: RunReference[];
}
export interface RunReference {
  id: string;
  kind: string;
  status: string;
  createdAt: string;
}
export interface RunPage {
  items: RunView[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}
/** Most runs one bulk deletion accepts (`MaxRunsPerDeletion` on the server). */
export const MAX_RUNS_PER_DELETION = 1000;
/** Settled run ids on every page, newest first; `hasMore` means more exist than one deletion takes. */
export interface DeletableRuns {
  ids: string[];
  hasMore: boolean;
}
export interface SelectionRequest {
  fileIds: string[];
  operationId: string;
  /** Rollback only: remove folders the undo leaves empty; omitted inherits the run's choice. */
  deleteEmptyFolders?: boolean;
}
export interface RollbackCheckProgress {
  completedFiles: number;
  totalFiles: number;
}
export interface SelectionPreview {
  files: {
    id: string;
    safe: boolean;
    currentPath: string;
    destinationPath: string;
    issues: RenameIssue[];
  }[];
}
export interface RecoveryPreview {
  runId: string;
  canRecover: boolean;
  action: string;
  files: {
    id: string;
    locatedPath: string | null;
    state: string;
    issues: RenameIssue[];
  }[];
}
export interface TmdbRequest {
  mediaType: string;
  query: string;
  language: string;
  id?: number;
  season?: number;
  episode?: number;
  fileId?: string;
  instanceId?: string;
  inputPath?: string;
  optionsHash?: string;
}
export interface TmdbMatch {
  id: number;
  mediaType: string;
  title: string;
  year: string | null;
  season: number | null;
  episode: number | null;
  episodeTitle: string | null;
  confirmationToken: string | null;
}
export interface MatchSuggestion {
  fileId: string;
  instanceId: string;
  inputPath: string;
  optionsHash: string;
  match: TmdbMatch | null;
  error: string;
}
export interface TmdbMatchRow {
  fileId: string;
  plugin: PluginInstance;
  position: number;
  suggestion: MatchSuggestion | undefined;
  step: StepTrace | undefined;
}
export interface MatchContext {
  fileId: string;
  plugin: PluginInstance;
  inputPath: string;
  state?: TmdbPickerState;
  reusedShow?: boolean;
}
export interface TmdbPickerState {
  query: string;
  mediaType: string;
  language: string;
  season: number;
  episode: number;
  matches: TmdbMatch[];
  selected: TmdbMatch | null;
}

@Injectable({ providedIn: 'root' })
export class RenameApi {
  private readonly http = inject(HttpClient);
  catalog() {
    return this.http.get<PluginDescriptor[]>('/api/rename-plugins');
  }
  preview(request: PipelineRequest) {
    return this.http.post<PreviewResult>('/api/rename-previews', request);
  }
  prepare(request: PipelineRequest) {
    return this.http.post<JobAccepted>('/api/rename-plans', request);
  }
  plan(id: string) {
    return this.http.get<PlanView>(`/api/rename-plans/${encodeURIComponent(id)}`);
  }
  execute(id: string, version: number, operationId: string) {
    return this.http.post<JobAccepted>(`/api/rename-plans/${encodeURIComponent(id)}/execute`, {
      version,
      operationId,
    });
  }
  runs(page: number) {
    return this.http.get<RunPage>('/api/rename-runs', { params: { page, pageSize: 20 } });
  }
  run(id: string) {
    return this.http.get<RunView>(`/api/rename-runs/${encodeURIComponent(id)}`);
  }
  deleteRun(id: string) {
    return this.http.delete<void>(`/api/rename-runs/${encodeURIComponent(id)}`);
  }
  deletableRuns() {
    return this.http.get<DeletableRuns>('/api/rename-run-deletions/candidates');
  }
  /** Deletes the runs (and their undo records) together; resolves to every deleted run id. */
  deleteRuns(runIds: string[]) {
    return this.http.post<string[]>('/api/rename-run-deletions', { runIds });
  }
  rollbackPreview(id: string, request: SelectionRequest) {
    return this.http.post<SelectionPreview>(
      `/api/rename-runs/${encodeURIComponent(id)}/rollback-preview`,
      request,
    );
  }
  rollbackCheckProgress(id: string, operationId: string) {
    return this.http.get<RollbackCheckProgress>(
      `/api/rename-runs/${encodeURIComponent(id)}/rollback-preview/${encodeURIComponent(operationId)}`,
    );
  }
  rollback(id: string, request: SelectionRequest) {
    return this.http.post<JobAccepted>(
      `/api/rename-runs/${encodeURIComponent(id)}/rollback`,
      request,
    );
  }
  recoveryPreview(id: string) {
    return this.http.post<RecoveryPreview>(
      `/api/rename-runs/${encodeURIComponent(id)}/recovery-preview`,
      {},
    );
  }
  recover(id: string, action: string, operationId: string) {
    return this.http.post<JobAccepted>(`/api/rename-runs/${encodeURIComponent(id)}/recover`, {
      action,
      operationId,
    });
  }
  tmdb(request: TmdbRequest) {
    return this.http.post<{ matches: TmdbMatch[] }>('/api/metadata/tmdb/search', request);
  }
}

export function isMatchingIssue(code: string): boolean {
  return code === 'MetadataRequired' || code === 'MetadataStale';
}

export function awaitingMetadata(file: PreviewFile): boolean {
  return file.issues.some((issue) => isMatchingIssue(issue.code));
}

export function isSettledRun(run: RunView): boolean {
  return run.status === 'Completed' || run.status === 'Recovered';
}

export function renameError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (!error.status) {
      return 'Cannot reach the server. Check your connection and retry.';
    }
    const problem = error.error as { detail?: string; title?: string; code?: string } | null;
    return (
      problem?.detail ??
      problem?.title ??
      `The request failed (${error.status}). Retry or check the server configuration.`
    );
  }
  return 'The request could not be completed. Retry.';
}

export function operationId(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 15) | 64;
  bytes[8] = (bytes[8] & 63) | 128;
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
