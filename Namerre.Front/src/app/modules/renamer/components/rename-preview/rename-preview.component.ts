import { Component, computed, input, output, signal } from '@angular/core';
import { PreviewFile, PreviewResult, awaitingMetadata, isMatchingIssue } from '../../rename-api';
import { NameChangeComponent } from '../name-change/name-change.component';

type PreviewStatus = 'rename' | 'unchanged' | 'problem';
type PreviewFilter = 'all' | PreviewStatus;

@Component({
  imports: [NameChangeComponent],
  selector: 'app-rename-preview',
  styleUrl: './rename-preview.component.scss',
  templateUrl: './rename-preview.component.html',
})
export class RenamePreviewComponent {
  readonly result = input<PreviewResult | null>(null);
  readonly loading = input(false);
  readonly disabled = input(false);
  readonly previewRequested = output<void>();
  protected readonly awaitingMetadata = awaitingMetadata;
  protected readonly isMatchingIssue = isMatchingIssue;
  protected readonly filter = signal<PreviewFilter>('all');
  protected readonly metadataPending = computed(() => this.result()?.files.some(awaitingMetadata));
  protected readonly unchanged = computed(
    () => this.result()?.canPrepare && this.result()?.files.every((file) => file.unchanged),
  );
  protected readonly counts = computed(() => {
    const counts = { all: 0, rename: 0, unchanged: 0, problem: 0 };
    for (const file of this.result()?.files ?? []) {
      counts.all++;
      counts[this.status(file)]++;
    }
    return counts;
  });
  protected readonly filters = computed(() => {
    const counts = this.counts();
    return [
      { id: 'all' as const, label: 'All', count: counts.all },
      { id: 'rename' as const, label: 'Will rename', count: counts.rename },
      { id: 'unchanged' as const, label: 'Unchanged', count: counts.unchanged },
      { id: 'problem' as const, label: 'Problems', count: counts.problem },
    ];
  });
  protected readonly visibleFiles = computed(() => {
    const filter = this.filter();
    const files = this.result()?.files ?? [];
    return filter === 'all' ? files : files.filter((file) => this.status(file) === filter);
  });

  protected status(file: PreviewFile): PreviewStatus {
    if (file.issues.length > 0 || file.steps.some((step) => step.error)) return 'problem';
    return file.unchanged ? 'unchanged' : 'rename';
  }

  protected problems(file: PreviewFile) {
    return file.issues.filter((issue) => !isMatchingIssue(issue.code));
  }
}
