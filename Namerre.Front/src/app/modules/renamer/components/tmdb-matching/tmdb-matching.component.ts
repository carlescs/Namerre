import { Component, computed, ElementRef, inject, input, output } from '@angular/core';
import {
  MatchSuggestion,
  StepTrace,
  TmdbMatch,
  TmdbMatchRow,
  isMatchingIssue,
} from '../../rename-api';
import { tmdbFilename } from '../../tmdb-filename';

type RowStatus = 'needs' | 'proposed' | 'confirmed' | 'problem' | 'waiting';

interface RowView {
  key: string;
  row: TmdbMatchRow;
  name: string;
  folder: string;
  status: RowStatus;
  summary: string;
  problem: string;
  code: string;
  proposal: MatchSuggestion | null;
}

const STATUS_LABELS: Record<RowStatus, string> = {
  needs: 'Needs match',
  proposed: 'Proposed',
  confirmed: 'Confirmed',
  problem: 'Problem',
  waiting: 'Waiting',
};

const STATUS_CLASSES: Record<RowStatus, string> = {
  needs: 'bg-amber-100 text-amber-900 dark:bg-amber-500/15 dark:text-amber-200',
  proposed: 'bg-indigo-100 text-indigo-900 dark:bg-indigo-500/20 dark:text-indigo-200',
  confirmed: 'bg-emerald-100 text-emerald-900 dark:bg-emerald-500/15 dark:text-emerald-200',
  problem: 'bg-rose-100 text-rose-900 dark:bg-rose-500/15 dark:text-rose-200',
  waiting: 'bg-slate-100 text-slate-700 dark:bg-slate-800 dark:text-slate-300',
};

@Component({
  imports: [],
  selector: 'app-tmdb-matching',
  styleUrl: './tmdb-matching.component.scss',
  templateUrl: './tmdb-matching.component.html',
})
export class TmdbMatchingComponent {
  readonly rows = input.required<TmdbMatchRow[]>();
  readonly suggesting = input(false);
  readonly suggestProgress = input({ done: 0, total: 0 });
  readonly loadingPreview = input(false);
  readonly hasPreview = input(false);
  readonly disabled = input(false);
  readonly canAcceptAll = input(false);
  readonly searchRequested = output<string>();
  readonly allAccepted = output<void>();
  readonly suggestionAccepted = output<MatchSuggestion>();
  readonly matchRequested = output<{ fileId: string; step: StepTrace }>();
  readonly previewRequested = output<void>();
  readonly fileRemoved = output<string>();
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly statusClasses = STATUS_CLASSES;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly views = computed(() => this.rows().map((row) => this.view(row)));
  /** Show the step number only when several TMDB steps make rows ambiguous. */
  protected readonly showSteps = computed(
    () => new Set(this.rows().map((row) => row.plugin.instanceId)).size > 1,
  );
  /** Files whose TMDB step still lacks a confirmed match; each one keeps review disabled. */
  protected readonly unmatchedFiles = computed(
    () =>
      new Set(
        this.rows()
          .filter((row) => this.needsMatch(row))
          .map((row) => row.fileId),
      ).size,
  );
  /** Episodes are the likely search when most files carry an SxxExx / 1x02 pattern. */
  protected readonly likelyEpisodes = computed(() => {
    const files = new Map(
      this.rows().map((row) => [row.fileId, row.step?.inputPath ?? row.fileId] as const),
    );
    const episodes = [...files.values()].filter(
      (path) => tmdbFilename(path, 'tv').season !== undefined,
    ).length;
    return episodes * 2 > files.size;
  });
  protected readonly busy = computed(
    () => this.suggesting() || this.loadingPreview() || this.disabled(),
  );

  protected needsMatch(row: TmdbMatchRow): boolean {
    return !!row.step?.error && isMatchingIssue(row.step.error.code);
  }

  protected showFirstUnmatched(): void {
    this.host.nativeElement.querySelector<HTMLElement>('article[data-unmatched]')?.focus();
  }

  private view(row: TmdbMatchRow): RowView {
    const index = row.fileId.lastIndexOf('/');
    const suggestion = row.suggestion;
    const step = row.step;
    const needsMatch = this.needsMatch(row);
    const verified =
      needsMatch && suggestion?.match?.confirmationToken && suggestion.inputPath === step?.inputPath
        ? suggestion
        : null;
    const otherError = step?.error && !needsMatch ? step.error : null;
    const problem = otherError ? otherError.message : (suggestion?.error ?? '');
    let status: RowStatus;
    if (!step) status = 'waiting';
    else if (problem) status = 'problem';
    else if (verified) status = 'proposed';
    else if (step.error) status = 'needs';
    else status = 'confirmed';
    const match = suggestion?.inputPath === step?.inputPath ? suggestion?.match : null;
    return {
      key: row.fileId + ':' + row.plugin.instanceId,
      row,
      name: row.fileId.slice(index + 1),
      folder: index > 0 ? row.fileId.slice(0, index) : '',
      status,
      summary: match ? this.describe(match) : '',
      problem,
      code: otherError?.code ?? '',
      proposal: verified,
    };
  }

  private describe(match: TmdbMatch): string {
    const title = match.year ? `${match.title} (${match.year})` : match.title;
    if (match.mediaType !== 'tv') return `${title} · Movie`;
    const pad = (value: number | null) => String(value ?? 0).padStart(2, '0');
    return [title, `S${pad(match.season)}E${pad(match.episode)}`, match.episodeTitle]
      .filter(Boolean)
      .join(' · ');
  }
}
