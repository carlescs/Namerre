import { computed, Injectable, signal } from '@angular/core';
import { loadPipelineDraft, savePipelineDraft } from '../modules/renamer/draft-storage';

export type WorkflowModeValue = 'simple' | 'advanced';

export const workflowModeStorageKey = 'namerre-workflow-mode';

const modes: readonly WorkflowModeValue[] = ['simple', 'advanced'];

/**
 * How the renamer is edited in this browser: Simple (preset cards, fewer stages) or Advanced (the
 * full plugin editor and run options). Only the editing UI changes; prepared plans and runs never do.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowMode {
  /** The choice saved in this browser; null until the user picks a mode. */
  readonly stored = signal<WorkflowModeValue | null>(readMode());
  /** Simple until the user picks a mode, also when the onboarding prompt was dismissed. */
  readonly mode = computed<WorkflowModeValue>(() => this.stored() ?? 'simple');
  /** A switch waiting for the user to accept that the draft's advanced steps are discarded. */
  readonly pending = signal<WorkflowModeValue | null>(null);
  private readonly dismissed = signal(false);
  /** Whether to ask for a mode: none is stored and the prompt was not dismissed in this visit. */
  readonly onboarding = computed(
    () => this.stored() === null && !this.dismissed() && this.pending() === null,
  );
  /** Increases each time the draft's steps are discarded to switch to Simple. */
  readonly discarded = signal(0);

  /**
   * Switches mode, unless the draft has steps that no Simple preset can show: the switch then
   * waits in `pending` until the user discards them (`confirm`) or keeps them (`cancel`).
   */
  async request(mode: WorkflowModeValue): Promise<void> {
    const plugins = loadPipelineDraft()?.plugins ?? [];
    if (mode === 'simple' && plugins.length) {
      // Loaded on demand: the presets belong to the lazily loaded renamer, not the app shell.
      const { simpleCompatible } = await import('../modules/renamer/simple-presets');
      if (!simpleCompatible(plugins)) {
        this.pending.set(mode);
        return;
      }
    }
    this.select(mode);
  }

  /** Discards the draft's steps and makes the pending switch. */
  confirm(): void {
    const mode = this.pending();
    if (!mode) return;
    // Simple's run options (see `simpleRunOptions`); the renamer applies them again when it opens.
    savePipelineDraft({
      plugins: [],
      verifyContent: false,
      deleteEmptyFolders: true,
      numberCollisions: false,
    });
    this.discarded.update((count) => count + 1);
    this.pending.set(null);
    this.select(mode);
  }

  cancel(): void {
    this.pending.set(null);
  }

  /** Closes the onboarding prompt without saving a choice; it is shown again on the next visit. */
  dismiss(): void {
    this.dismissed.set(true);
  }

  private select(mode: WorkflowModeValue): void {
    this.stored.set(mode);
    try {
      localStorage.setItem(workflowModeStorageKey, mode);
    } catch {
      // Storage can be unavailable (private mode, blocked site data); the choice still applies.
    }
  }
}

function readMode(): WorkflowModeValue | null {
  try {
    const stored = localStorage.getItem(workflowModeStorageKey) as WorkflowModeValue | null;
    return stored && modes.includes(stored) ? stored : null;
  } catch {
    return null;
  }
}
