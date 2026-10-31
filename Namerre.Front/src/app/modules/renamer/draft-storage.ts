import type { PluginInstance } from './rename-api';
import type { RenamerFile } from './renamer-file';

// The unfinished workflow survives reloads in this browser only. TMDB confirmations are not kept:
// their tokens are bound to a preview, so matching is simply redone.
const filesKey = 'namerre.draft.files.v1';
const pipelineKey = 'namerre.draft.pipeline.v1';
const relatedKey = 'namerre.draft.related.v1';

export interface PipelineDraft {
  plugins: PluginInstance[];
  verifyContent: boolean;
  deleteEmptyFolders: boolean;
}

export function loadFilesDraft(): RenamerFile[] {
  const files = read(filesKey);
  return Array.isArray(files) ? files.filter(isFile) : [];
}

export function saveFilesDraft(files: RenamerFile[]): void {
  write(filesKey, files.length ? files : null);
}

/** Whether related files (subtitles, .nfo, artwork) follow their video; on unless turned off. */
export function loadIncludeRelatedDraft(): boolean {
  return read(relatedKey) !== false;
}

export function saveIncludeRelatedDraft(includeRelated: boolean): void {
  write(relatedKey, includeRelated ? null : false);
}

export function loadPipelineDraft(): PipelineDraft | null {
  const draft = read(pipelineKey) as Partial<PipelineDraft> | null;
  if (!draft || typeof draft !== 'object' || !Array.isArray(draft.plugins)) return null;
  return {
    plugins: draft.plugins.filter(isPlugin),
    verifyContent: draft.verifyContent === true,
    deleteEmptyFolders: draft.deleteEmptyFolders === true,
  };
}

export function savePipelineDraft(draft: PipelineDraft): void {
  write(
    pipelineKey,
    draft.plugins.length || draft.verifyContent || draft.deleteEmptyFolders ? draft : null,
  );
}

function read(key: string): unknown {
  try {
    const value = localStorage.getItem(key);
    return value ? JSON.parse(value) : null;
  } catch {
    return null;
  }
}

function write(key: string, value: unknown): void {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Storage can be full or blocked (private windows); the draft is a convenience only.
  }
}

function isFile(value: unknown): value is RenamerFile {
  const file = value as RenamerFile;
  return (
    !!file &&
    typeof file.path === 'string' &&
    typeof file.name === 'string' &&
    typeof file.folderPath === 'string' &&
    typeof file.length === 'number' &&
    typeof file.lastModifiedUtc === 'string'
  );
}

function isPlugin(value: unknown): value is PluginInstance {
  const plugin = value as PluginInstance;
  return (
    !!plugin &&
    typeof plugin.instanceId === 'string' &&
    typeof plugin.typeId === 'string' &&
    typeof plugin.version === 'number' &&
    typeof plugin.enabled === 'boolean' &&
    !!plugin.options &&
    typeof plugin.options === 'object'
  );
}
