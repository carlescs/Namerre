import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { Subscription } from 'rxjs';
import { RenameApi, renameError } from '../../rename-api';
import type { PlanView } from '../../rename-models';
import { optionEntries, pluginName } from '../run-history/run-display';

/** The run's saved settings and file fingerprints, read once when first opened. */
@Component({
  selector: 'app-run-plan-snapshot',
  templateUrl: './run-plan-snapshot.component.html',
})
export class RunPlanSnapshotComponent {
  readonly planId = input.required<string>();
  private readonly api = inject(RenameApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly plan = signal<PlanView | null>(null);
  protected readonly error = signal('');
  protected readonly loading = signal(false);
  protected readonly optionEntries = optionEntries;
  protected readonly pluginName = pluginName;
  private request?: Subscription;

  constructor() {
    effect(() => {
      this.planId();
      untracked(() => {
        this.request?.unsubscribe();
        this.plan.set(null);
        this.error.set('');
        this.loading.set(false);
      });
    });
  }

  protected inspect(): void {
    const id = this.planId();
    if (!id || this.plan() || this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.request = this.api
      .plan(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (plan) => {
          if (id !== this.planId()) return;
          this.plan.set(plan);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          if (id !== this.planId()) return;
          this.loading.set(false);
          this.error.set(renameError(error));
        },
      });
  }
}
