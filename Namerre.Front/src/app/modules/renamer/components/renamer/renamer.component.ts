import { Component, signal } from '@angular/core';
import { RenamerFile } from '../../renamer-file';
import { FileSelectionComponent } from '../file-selection/file-selection.component';
import { RenameWorkflowComponent } from '../rename-workflow/rename-workflow.component';

@Component({
  selector: 'app-renamer',
  imports: [FileSelectionComponent, RenameWorkflowComponent],
  styleUrl: './renamer.component.scss',
  templateUrl: './renamer.component.html',
})
export class RenamerComponent {
  protected readonly files = signal<RenamerFile[]>([]);

  protected removeFile(path: string): void {
    this.files.update((files) => files.filter((file) => file.path !== path));
  }
}
