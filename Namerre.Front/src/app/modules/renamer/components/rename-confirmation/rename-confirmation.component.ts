import {
  afterNextRender,
  Component,
  computed,
  type ElementRef,
  Injector,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCheck, faPencil, faRotateLeft, faXmark } from '@fortawesome/free-solid-svg-icons';
import type { PlanView, TargetOverride } from '../../rename-api';
import { NameChangeComponent } from '../name-change/name-change.component';

@Component({
  imports: [FaIconComponent, NameChangeComponent],
  selector: 'app-rename-confirmation',
  styleUrl: './rename-confirmation.component.scss',
  templateUrl: './rename-confirmation.component.html',
})
export class RenameConfirmationComponent {
  private readonly injector = inject(Injector);
  readonly plan = input<PlanView | null>(null);
  readonly fileCount = input(0);
  readonly verifyContent = input(true);
  readonly preparing = input(false);
  readonly executing = input(false);
  readonly canConfirm = input(false);
  readonly canRetry = input(false);
  /** Whether new names can be edited by hand; editing prepares the plan again. */
  readonly canEdit = input(false);
  /** A hand-edited name is being checked before the plan is prepared again. */
  readonly checkingEdit = input(false);
  readonly confirmRequested = output<void>();
  readonly retryRequested = output<void>();
  readonly targetEdited = output<TargetOverride>();
  readonly editReset = output<string>();
  private readonly editor = viewChild<ElementRef<HTMLInputElement>>('editor');
  protected readonly pencilIcon = faPencil;
  protected readonly saveIcon = faCheck;
  protected readonly cancelIcon = faXmark;
  protected readonly resetIcon = faRotateLeft;
  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal('');
  protected readonly changedCount = computed(
    () => this.plan()?.files.filter((file) => file.sourcePath !== file.targetPath).length ?? 0,
  );
  protected readonly changedLabel = computed(() => (this.changedCount() === 1 ? 'file' : 'files'));
  protected readonly completed = computed(() => this.plan()?.completedFiles ?? 0);
  protected readonly percent = computed(() =>
    this.fileCount() > 0 ? Math.min(100, (this.completed() / this.fileCount()) * 100) : 0,
  );
  protected readonly overridden = computed(
    () => new Set(this.plan()?.pipeline.overrides?.map((entry) => entry.fileId)),
  );

  protected startEdit(fileId: string, targetPath: string): void {
    if (!this.canEdit()) return;
    this.editing.set(fileId);
    this.draft.set(targetPath);
    afterNextRender(() => this.editor()?.nativeElement.select(), { injector: this.injector });
  }

  protected saveEdit(fileId: string, targetPath: string): void {
    const draft = this.draft().trim();
    if (!draft) return;
    this.editing.set(null);
    if (draft !== targetPath) this.targetEdited.emit({ fileId, targetPath: draft });
  }

  protected cancelEdit(): void {
    this.editing.set(null);
  }
}
