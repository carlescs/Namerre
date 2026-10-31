import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

/** Value of one column in a row of a `list` option. */
export type PluginOptionCell = string | boolean | number | string[] | null;
export type PluginOptionRow = Record<string, PluginOptionCell>;
/** `number` options hold a number (null while the editor's input is empty), `multiSelect` a string array. */
export type PluginOptionValue = PluginOptionCell | PluginOptionRow[];
export interface PluginOptions {
  [key: string]: PluginOptionValue | undefined;
  // TMDB options read outside the generic editor (matching stage, template preview, options hash).
  preset?: string;
  nameTemplate?: string;
  changeFolder?: boolean;
  baseFolder?: string;
  folderTemplate?: string;
  seasonFolder?: boolean;
  seasonFolderTemplate?: string;
  language?: string;
  /** How `{episode}` writes a multi-episode match: `repeat` (01-E02), `concatenate` (01E02), `hyphen` (01-02). */
  episodeRange?: string;
}
/**
 * Limits a step to files whose current extension is listed and whose current name matches
 * `pattern` (empty parts do not restrict); `invert` applies it to the other files instead.
 */
export interface StepCondition {
  extensions?: string[];
  pattern?: string;
  /** Pattern matching ignores case unless this is false. */
  ignoreCase?: boolean;
  invert?: boolean;
}
export interface PluginInstance {
  instanceId: string;
  typeId: string;
  version: number;
  enabled: boolean;
  options: PluginOptions;
  /** Absent or empty applies the step to every file. */
  condition?: StepCondition | null;
}
export interface OptionChoice {
  value: string;
  label: string;
}
export type OptionFieldKind =
  | 'text'
  | 'checkbox'
  | 'select'
  | 'number'
  | 'textarea'
  | 'list'
  | 'multiSelect';
export interface OptionField {
  key: string;
  label: string;
  kind: OptionFieldKind;
  /** Options of a `select` or `multiSelect` field. */
  choices?: OptionChoice[];
  hint?: string;
  placeholder?: string;
  /** Values offered while typing in a text field. */
  suggestions?: string[];
  /** Spans the whole row instead of one column. Checkboxes, text areas and lists always do. */
  wide?: boolean;
  /** Bounds of a `number` field. */
  min?: number;
  max?: number;
  /** A `number` field accepts `min` (or 0) plus multiples of this. */
  step?: number;
  /** Columns of each row of a `list` field; lists do not nest. */
  fields?: OptionField[];
  /**
   * Hidden (not removed) unless this select field holds one of the values, or this multi-select
   * includes one; its value is still sent.
   */
  visibleWhen?: OptionCondition;
}
export interface OptionCondition {
  key: string;
  values: string[];
}
export interface OptionGroup {
  heading?: string;
  /** Hidden (not removed) unless all of these checkbox options are on; its values are still sent. */
  requires?: string[];
  fields: OptionField[];
}
export interface PluginDescriptor {
  typeId: string;
  version: number;
  name: string;
  description: string;
  defaultOptions: PluginOptions;
  /** Option editor layout; keys refer to `defaultOptions`. */
  form: OptionGroup[];
  /** At most one instance of this type per pipeline, enabled or not. */
  singleInstance?: boolean;
}
export interface MetadataSelection {
  fileId: string;
  instanceId: string;
  inputPath: string;
  optionsHash: string;
  confirmationToken: string;
}
/** A target path typed by the user that replaces the pipeline's result for one file. */
export interface TargetOverride {
  fileId: string;
  targetPath: string;
}
export interface PipelineRequest {
  files: { id: string; path: string }[];
  plugins: PluginInstance[];
  selections: MetadataSelection[];
  /** False records size and modification time instead of hashing file contents (faster, weaker). */
  verifyContent?: boolean;
  /** True removes source folders that the run leaves empty once every file is placed. */
  deleteEmptyFolders?: boolean;
  /** Manual target paths applied after the plugins; they may change any part of the path. */
  overrides?: TargetOverride[];
  /** True renames subtitles, .nfo files and artwork together with the video they belong to. */
  includeCompanions?: boolean;
  /** `AddNumber` gives colliding names a ` (1)`, ` (2)`, … suffix; `Report` (default) blocks them. */
  onCollision?: 'Report' | 'AddNumber';
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
  /** The step's condition did not match, so the path passed through unchanged. */
  skipped?: boolean;
}
export interface PreviewFile {
  id: string;
  sourcePath: string;
  targetPath: string;
  unchanged: boolean;
  steps: StepTrace[];
  issues: RenameIssue[];
  /** ID of the video this related file follows. */
  companionOf?: string | null;
  /** The name received a ` (n)` suffix to avoid a collision. */
  numbered?: boolean;
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
  files: {
    id: string;
    sourcePath: string;
    targetPath: string;
    hash: string;
    size: number;
    companionOf?: string | null;
  }[];
  issues: RenameIssue[];
  createdAt: string;
  /** Source file facts by file ID, read once when preparation starts; absent on older plans. */
  facts?: Record<string, FileFacts>;
  /** What started the plan: absent for a person, `watch:<name>` for a watch folder. */
  origin?: string | null;
}
export interface FileFacts {
  size: number;
  modifiedUtc: string;
  createdUtc: string | null;
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
  /** ID of the video this related file followed. */
  companionOf?: string | null;
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
  /** What started the run: absent for a person, `watch:<name>` for a watch folder. */
  origin?: string | null;
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
  /** Last episode of a multi-episode file; consecutive with `episode`, at most 10 episodes. */
  episodeEnd?: number;
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
  // Present on confirmed matches: original-language title, episode air date or movie release date
  // (yyyy-MM-dd) and the show's first-air year (TV only).
  originalTitle?: string | null;
  airDate?: string | null;
  showYear?: string | null;
  /** Last episode of a confirmed multi-episode match; `episodeTitle` then joins the titles. */
  episodeEnd?: number | null;
}
export interface MatchSuggestion {
  fileId: string;
  instanceId: string;
  inputPath: string;
  optionsHash: string;
  match: TmdbMatch | null;
  error: string;
  /** 0–1; proposals at or above `HIGH_CONFIDENCE` can be accepted together. */
  confidence?: number;
}
/** Mirrors `MatchConfidence.High` on the server. */
export const HIGH_CONFIDENCE = 0.85;
export interface TmdbSuggestionRequest {
  mediaType: string;
  language: string;
  fileId: string;
  instanceId: string;
  /** The TMDB step's input as the preview traced it; the server reads the title from it. */
  inputPath: string;
  optionsHash: string;
}
/** What a file name says about its movie or episode, as the server read it. */
export interface ParsedFileName {
  query: string;
  year: string | null;
  season: number | null;
  episode: number | null;
  episodeEnd: number | null;
}
export interface TmdbSuggestion {
  parsed: ParsedFileName;
  /** The search results the proposal was chosen from. */
  candidates: TmdbMatch[];
  /** Verified and bound to the request, but only used once accepted. */
  match: TmdbMatch | null;
  confidence: number;
  /** Why there is no match. */
  problem: string | null;
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
  /** Last episode of a multi-episode file, or null for one episode. */
  episodeEnd?: number | null;
  matches: TmdbMatch[];
  selected: TmdbMatch | null;
}

/** Step list and run options a preset stores; TMDB matches are per file and never included. */
export interface SavePresetRequest {
  name: string;
  plugins: PluginInstance[];
  verifyContent: boolean;
  deleteEmptyFolders: boolean;
  onCollision: 'Report' | 'AddNumber';
}
export interface PresetSummary {
  id: string;
  name: string;
  steps: number;
  updatedAt: string;
}
export interface PresetView {
  id: string;
  name: string;
  plugins: PluginInstance[];
  verifyContent: boolean;
  deleteEmptyFolders: boolean;
  onCollision: 'Report' | 'AddNumber' | null;
  createdAt: string;
  updatedAt: string;
  /** Instance ids of steps whose plugin type or version this server no longer has. */
  unknownSteps: string[];
}

export interface WatchedFileProblem {
  path: string;
  /** `Failed` is retried at `nextAttemptAt`; `Ignored` waits until the file changes. */
  status: 'Failed' | 'Ignored';
  attempts: number;
  error: string | null;
  nextAttemptAt: string | null;
  updatedAt: string;
}
export interface WatchFolderStatus {
  name: string;
  path: string;
  preset: string;
  paused: boolean;
  lastScanAt: string | null;
  inFlight: number;
  completed: number;
  /** Why the folder cannot run, e.g. a missing preset. */
  error: string | null;
  problems: WatchedFileProblem[];
  /** TMDB suggestions at or above this confidence are accepted without a person. */
  tmdbAutoConfirm: number | null;
}

/** The watch folder name of a run or plan started by one, or null. */
export function watchFolderOrigin(origin: string | null | undefined): string | null {
  return origin?.startsWith('watch:') ? origin.slice('watch:'.length) : null;
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
