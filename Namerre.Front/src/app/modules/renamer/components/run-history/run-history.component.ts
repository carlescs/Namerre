import {
  afterNextRender,
  Component,
  DestroyRef,
  type ElementRef,
  Injector,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Auth } from '../../../../auth/auth';
import { RunDetailComponent } from '../run-detail/run-detail.component';
import { RunListComponent } from '../run-list/run-list.component';

/** The history page: the run list at `/runs`, one run's details at `/runs/:id`. */
@Component({
  imports: [RouterLink, RunDetailComponent, RunListComponent],
  selector: 'app-run-history',
  styleUrl: './run-history.component.scss',
  templateUrl: './run-history.component.html',
})
export class RunHistoryComponent {
  protected readonly auth = inject(Auth);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('pageHeading');
  protected readonly runId = signal('');

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      this.runId.set(params.get('id') ?? '');
      afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
    });
  }
}
