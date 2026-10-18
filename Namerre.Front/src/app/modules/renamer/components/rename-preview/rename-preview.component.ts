import { Component, computed, input, output } from '@angular/core';
import { PreviewResult, awaitingMetadata, isMatchingIssue } from '../../rename-api';

@Component({
  imports: [],
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
  protected readonly metadataPending = computed(() => this.result()?.files.some(awaitingMetadata));
  protected readonly unchanged = computed(
    () => this.result()?.canPrepare && this.result()?.files.every((file) => file.unchanged),
  );

  protected folder(path: string): string {
    const separator = path.lastIndexOf('/');
    return separator < 0 ? 'Server root' : path.slice(0, separator);
  }
}
