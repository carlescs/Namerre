import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  DeletableRuns,
  JobAccepted,
  PipelineRequest,
  PlanView,
  PluginDescriptor,
  PresetSummary,
  PresetView,
  PreviewResult,
  RecoveryPreview,
  RollbackCheckProgress,
  RunPage,
  RunView,
  SavePresetRequest,
  SelectionPreview,
  SelectionRequest,
  TmdbMatch,
  TmdbRequest,
  TmdbSuggestion,
  TmdbSuggestionRequest,
  WatchFolderStatus,
} from './rename-models';

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
  /** Same-origin download address of a run's history export. */
  exportUrl(id: string, format: 'csv' | 'json') {
    return `/api/rename-runs/${encodeURIComponent(id)}/export?format=${format}`;
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
  presets() {
    return this.http.get<PresetSummary[]>('/api/rename-presets');
  }
  preset(id: string) {
    return this.http.get<PresetView>(`/api/rename-presets/${encodeURIComponent(id)}`);
  }
  createPreset(request: SavePresetRequest) {
    return this.http.post<PresetView>('/api/rename-presets', request);
  }
  updatePreset(id: string, request: SavePresetRequest) {
    return this.http.put<PresetView>(`/api/rename-presets/${encodeURIComponent(id)}`, request);
  }
  renamePreset(id: string, name: string) {
    return this.http.patch<PresetView>(`/api/rename-presets/${encodeURIComponent(id)}`, { name });
  }
  deletePreset(id: string) {
    return this.http.delete<void>(`/api/rename-presets/${encodeURIComponent(id)}`);
  }
  watchFolders() {
    return this.http.get<WatchFolderStatus[]>('/api/watch-folders');
  }
  setWatchFolderPaused(name: string, paused: boolean) {
    return this.http.post<WatchFolderStatus>(
      `/api/watch-folders/${encodeURIComponent(name)}/${paused ? 'pause' : 'resume'}`,
      {},
    );
  }
  retryWatchedFile(name: string, path: string) {
    return this.http.post<WatchFolderStatus>(
      `/api/watch-folders/${encodeURIComponent(name)}/retry`,
      { path },
    );
  }
  /** Proposes a verified match from the step input's file name; nothing is accepted automatically. */
  suggestTmdb(request: TmdbSuggestionRequest) {
    return this.http.post<TmdbSuggestion>('/api/metadata/tmdb/suggestions', request);
  }
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
