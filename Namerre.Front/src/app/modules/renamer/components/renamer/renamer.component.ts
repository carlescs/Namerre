import { Component, effect, signal } from '@angular/core';
import {
  loadFilesDraft,
  loadIncludeRelatedDraft,
  saveFilesDraft,
  saveIncludeRelatedDraft,
} from '../../draft-storage';
import type { RenamerFile } from '../../renamer-file';
import { FileSelectionComponent } from '../file-selection/file-selection.component';
import { RenameWorkflowComponent } from '../rename-workflow/rename-workflow.component';

@Component({
  selector: 'app-renamer',
  imports: [FileSelectionComponent, RenameWorkflowComponent],
  styleUrl: './renamer.component.scss',
  templateUrl: './renamer.component.html',
})
export class RenamerComponent {
  protected readonly files = signal<RenamerFile[]>(loadFilesDraft());
  protected readonly restoredCount = this.files().length;
  protected readonly includeRelated = signal(loadIncludeRelatedDraft());

  constructor() {
    effect(() => saveFilesDraft(this.files()));
    effect(() => saveIncludeRelatedDraft(this.includeRelated()));
  }

  protected removeFile(path: string): void {
    this.files.update((files) => files.filter((file) => file.path !== path));
  }
}
