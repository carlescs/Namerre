import { Component, input, output } from '@angular/core';
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
  protected readonly isMatchingIssue = isMatchingIssue;
}
