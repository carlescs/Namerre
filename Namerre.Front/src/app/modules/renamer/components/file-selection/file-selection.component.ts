import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFolderOpen, faTrashCan } from '@fortawesome/free-solid-svg-icons';
import { formatFileSize } from '../../file-size';
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
  protected readonly formatSize = formatFileSize;
  protected readonly search = signal('');
  protected readonly typeFilter = signal<string | null>(null);
  protected readonly notice = signal('');
  protected readonly fileTypes = computed(() => {
    const counts = new Map<string, number>();
    for (const file of this.files()) {
      const type = extensionOf(file.name);
      counts.set(type, (counts.get(type) ?? 0) + 1);
    }
    return [...counts]
      .map(([type, count]) => ({ type, count, label: type || 'no extension' }))
      .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
  });
  /** The chosen file type, ignored once no file of that type is left. */
  protected readonly activeType = computed(() => {
    const type = this.typeFilter();
    return type !== null && this.fileTypes().some((entry) => entry.type === type) ? type : null;
  });
  protected readonly filteredFiles = computed(() => {
    const query = this.search().trim().toLowerCase();
    const type = this.activeType();
    return this.files().filter(
      (file) =>
        (type === null || extensionOf(file.name) === type) &&
        `${file.folderPath || 'Server root'}/${file.name}`.toLowerCase().includes(query),
    );
  });
  protected readonly filtering = computed(
    () => this.activeType() !== null || this.search().trim() !== '',
  );
  protected readonly removeShownLabel = computed(() => {
    const count = this.filteredFiles().length;
    const noun = count === 1 ? 'file' : 'files';
    return this.activeType() !== null
      ? `Remove these ${count} ${noun}`
      : `Remove ${count} shown ${noun}`;
  });

  protected clearFiles(): void {
    this.resetFilters();
    this.notice.set('');
    this.filesChanged.emit([]);
  }

  protected removeFile(path: string): void {
    this.filesChanged.emit(this.files().filter((file) => file.path !== path));
  }

  protected toggleType(type: string): void {
    this.typeFilter.set(this.activeType() === type ? null : type);
  }

  /** Removes exactly the files currently shown by the filters, never the hidden ones. */
  protected removeShown(): void {
    const shown = new Set(this.filteredFiles().map((file) => file.path));
    if (!shown.size) {
      return;
    }
    this.resetFilters();
    this.notice.set(`Removed ${shown.size} ${shown.size === 1 ? 'file' : 'files'}.`);
    this.filesChanged.emit(this.files().filter((file) => !shown.has(file.path)));
  }

  protected addFolder(folders: FolderListing[]): void {
    this.resetFilters();
    const files = this.files();
    const paths = new Set(files.map((file) => file.path));
    const additions: RenamerFile[] = [];
    let found = 0;
    for (const folder of folders) {
      for (const file of folder.files) {
        found++;
        const path = folder.path ? `${folder.path}/${file.name}` : file.name;
        if (!paths.has(path)) {
          paths.add(path);
          additions.push({ ...file, path, folderPath: folder.path });
        }
      }
    }
    const label = folders[0]?.path || 'Server root';
    if (additions.length) {
      this.notice.set(
        `Added ${additions.length} ${additions.length === 1 ? 'file' : 'files'} from ${label}.`,
      );
    } else if (found) {
      this.notice.set('No new files were added; they are already in the collection.');
    } else {
      this.notice.set(`No files were found in ${label}.`);
    }
    this.filesChanged.emit([...files, ...additions]);
  }

  protected resetFilters(): void {
    this.search.set('');
    this.typeFilter.set(null);
  }
}

function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot + 1).toLowerCase() : '';
}
