import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription } from 'rxjs';
import { tmdbOptionsHash } from '../../options-hash';
import { tmdbFilename } from '../../tmdb-filename';
import {
  MatchContext,
  MetadataSelection,
  RenameApi,
  TmdbMatch,
  TmdbPickerState,
  TmdbRequest,
  renameError,
} from '../../rename-api';

@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-tmdb-picker',
  styleUrl: './tmdb-picker.component.scss',
  templateUrl: './tmdb-picker.component.html',
})
export class TmdbPickerComponent implements OnInit, AfterViewInit {
  readonly context = input.required<MatchContext>();
  readonly confirmed = output<MetadataSelection>();
  readonly dismissed = output<void>();
  readonly stateChanged = output<TmdbPickerState>();
  private readonly api = inject(RenameApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly opener = document.activeElement as HTMLElement | null;
  protected readonly form = new FormGroup({
    query: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    mediaType: new FormControl('movie', { nonNullable: true }),
    language: new FormControl('en-US', { nonNullable: true, validators: [Validators.required] }),
    season: new FormControl(1, {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.min(0),
        Validators.max(999),
        Validators.pattern(/^\d+$/),
      ],
    }),
    episode: new FormControl(1, {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.min(1),
        Validators.max(9999),
        Validators.pattern(/^\d+$/),
      ],
    }),
  });
  protected readonly matches = signal<TmdbMatch[]>([]);
  protected readonly selected = signal<TmdbMatch | null>(null);
  protected readonly resolved = signal<TmdbMatch | null>(null);
  protected readonly busy = signal(false);
  protected readonly searched = signal(false);
  protected readonly error = signal('');
  private request?: Subscription;
  private revision = 0;
  private searchIdentity = '';

  constructor() {
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      const value = this.form.getRawValue();
      const identity = JSON.stringify([value.query, value.mediaType, value.language]);
      this.revision++;
      this.request?.unsubscribe();
      this.busy.set(false);
      this.resolved.set(null);
      this.error.set('');
      if (identity !== this.searchIdentity) {
        this.searchIdentity = identity;
        this.matches.set([]);
        this.selected.set(null);
        this.searched.set(false);
      }
    });
    this.destroyRef.onDestroy(() => {
      this.request?.unsubscribe();
      this.opener?.focus();
    });
  }

  ngOnInit(): void {
    const context = this.context();
    const inferred = tmdbFilename(context.inputPath, context.state?.mediaType ?? 'movie');
    this.form.patchValue({
      query: inferred.query,
      ...(inferred.season !== undefined
        ? { season: inferred.season, episode: inferred.episode }
        : {}),
      language: context.plugin.options.language ?? 'en-US',
      ...context.state,
    });
    if (context.state) {
      this.matches.set(context.state.matches);
      this.selected.set(context.state.selected);
      this.searched.set(context.state.matches.length > 0);
    }
  }

  ngAfterViewInit(): void {
    this.dialog().nativeElement.showModal();
  }

  protected search(): void {
    if (!this.validSearch()) return;
    this.request?.unsubscribe();
    const revision = ++this.revision;
    this.busy.set(true);
    this.error.set('');
    this.resolved.set(null);
    this.selected.set(null);
    this.matches.set([]);
    this.request = this.api
      .tmdb(this.searchRequest())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (revision !== this.revision) return;
          this.matches.set(result.matches);
          this.searched.set(true);
          this.busy.set(false);
        },
        error: (error: unknown) => {
          if (revision !== this.revision) return;
          this.error.set(renameError(error));
          this.busy.set(false);
        },
      });
  }

  protected choose(match: TmdbMatch): void {
    this.selected.set(match);
    this.resolved.set(null);
  }

  protected verify(): void {
    const match = this.selected();
    if (!match || !this.validRequest() || this.busy()) return;
    const context = this.context();
    const revision = ++this.revision;
    this.busy.set(true);
    this.error.set('');
    const request: TmdbRequest = {
      ...this.searchRequest(true),
      id: match.id,
      fileId: context.fileId,
      instanceId: context.plugin.instanceId,
      inputPath: context.inputPath,
      optionsHash: tmdbOptionsHash(context.plugin.options),
    };
    this.request = this.api
      .tmdb(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (revision !== this.revision) return;
          const verified = result.matches.find(
            (entry) => entry.id === match.id && entry.confirmationToken,
          );
          this.resolved.set(verified ?? null);
          this.busy.set(false);
          if (!verified)
            this.error.set(
              'TMDB did not validate this match. Correct the search or episode and retry.',
            );
        },
        error: (error: unknown) => {
          if (revision !== this.revision) return;
          this.error.set(renameError(error));
          this.busy.set(false);
        },
      });
  }

  protected confirm(): void {
    const match = this.resolved();
    if (!match?.confirmationToken || this.busy()) return;
    const context = this.context();
    this.rememberState();
    this.confirmed.emit({
      fileId: context.fileId,
      instanceId: context.plugin.instanceId,
      inputPath: context.inputPath,
      optionsHash: tmdbOptionsHash(context.plugin.options),
      confirmationToken: match.confirmationToken,
    });
  }

  protected cancel(): void {
    this.revision++;
    this.request?.unsubscribe();
    this.rememberState();
    this.dialog().nativeElement.close();
    this.dismissed.emit();
  }

  protected validRequest(): boolean {
    const controls = this.form.controls;
    return (
      this.validSearch() &&
      (controls.mediaType.value !== 'tv' || (controls.season.valid && controls.episode.valid))
    );
  }

  protected validSearch(): boolean {
    return this.form.controls.query.valid && this.form.controls.language.valid;
  }

  private rememberState(): void {
    this.stateChanged.emit({
      ...this.form.getRawValue(),
      matches: this.matches(),
      selected: this.resolved() ?? this.selected(),
    });
  }

  private searchRequest(includeEpisode = false): TmdbRequest {
    const value = this.form.getRawValue();
    return {
      mediaType: value.mediaType,
      query: value.query,
      language: value.language,
      ...(includeEpisode && value.mediaType === 'tv'
        ? { season: value.season, episode: value.episode }
        : {}),
    };
  }
}
