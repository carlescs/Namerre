import { Component, computed, ElementRef, inject, input, output } from '@angular/core';
import { MatchSuggestion, StepTrace, TmdbMatchRow, isMatchingIssue } from '../../rename-api';

@Component({
  imports: [],
  selector: 'app-tmdb-matching',
  styleUrl: './tmdb-matching.component.scss',
  templateUrl: './tmdb-matching.component.html',
})
export class TmdbMatchingComponent {
  readonly rows = input.required<TmdbMatchRow[]>();
  readonly searchType = input('movie');
  readonly suggesting = input(false);
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
  protected readonly isMatchingIssue = isMatchingIssue;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  /** Files whose TMDB step still lacks a confirmed match; each one keeps review disabled. */
  protected readonly unmatchedFiles = computed(
    () =>
      new Set(
        this.rows()
          .filter((row) => this.needsMatch(row))
          .map((row) => row.fileId),
      ).size,
  );

  protected needsMatch(row: TmdbMatchRow): boolean {
    return !!row.step?.error && isMatchingIssue(row.step.error.code);
  }

  protected showFirstUnmatched(): void {
    this.host.nativeElement.querySelector<HTMLElement>('article[data-unmatched]')?.focus();
  }
}
