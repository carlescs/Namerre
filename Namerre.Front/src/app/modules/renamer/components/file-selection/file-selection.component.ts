import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFolderOpen, faTrashCan } from '@fortawesome/free-solid-svg-icons';
import { FolderListing } from '../../folder-listing';
import { RenamerFile } from '../../renamer-file';
import { FolderPickerComponent } from '../folder-picker/folder-picker.component';

@Component({
  imports: [DatePipe, DecimalPipe, FaIconComponent, FolderPickerComponent],
  selector: 'app-file-selection',
  styleUrl: './file-selection.component.scss',
  templateUrl: './file-selection.component.html',
})
export class FileSelectionComponent {
  readonly files = input.required<RenamerFile[]>();
  readonly filesChanged = output<RenamerFile[]>();
  protected readonly trashIcon = faTrashCan;
  protected readonly folderIcon = faFolderOpen;
  protected readonly search = signal('');
  protected readonly filteredFiles = computed(() => {
    const query = this.search().trim().toLowerCase();
    return this.files().filter((file) =>
      `${file.folderPath || 'Server root'}/${file.name}`.toLowerCase().includes(query),
    );
  });

  protected clearFiles(): void {
    this.search.set('');
    this.filesChanged.emit([]);
  }

  protected removeFile(path: string): void {
    this.filesChanged.emit(this.files().filter((file) => file.path !== path));
  }

  protected addFolder(folders: FolderListing[]): void {
    this.search.set('');
    const files = this.files();
    const paths = new Set(files.map((file) => file.path));
    const additions: RenamerFile[] = [];
    for (const folder of folders) {
      for (const file of folder.files) {
        const path = folder.path ? `${folder.path}/${file.name}` : file.name;
        if (!paths.has(path)) {
          paths.add(path);
          additions.push({ ...file, path, folderPath: folder.path });
        }
      }
    }
    this.filesChanged.emit([...files, ...additions]);
  }
}
