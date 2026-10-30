import {
  CdkDrag,
  type CdkDragDrop,
  CdkDragHandle,
  CdkDragPreview,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { DatePipe, DecimalPipe } from '@angular/common';
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
import {
  faArrowDown,
  faArrowUp,
  faFolderOpen,
  faGripVertical,
  faTrashCan,
} from '@fortawesome/free-solid-svg-icons';
import { formatFileSize } from '../../file-size';
import type { FolderListing } from '../../folder-listing';
import type { RenamerFile } from '../../renamer-file';
import { FolderPickerComponent } from '../folder-picker/folder-picker.component';

/** Natural order: `file2` before `file10`, ignoring case and accents. */
const natural = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

interface SortOption {
  value: string;
  label: string;
  compare: (a: RenamerFile, b: RenamerFile) => number;
}

const byName = (a: RenamerFile, b: RenamerFile) =>
  natural.compare(a.name, b.name) || natural.compare(a.folderPath, b.folderPath);
const byFolder = (a: RenamerFile, b: RenamerFile) =>
  natural.compare(a.folderPath, b.folderPath) || natural.compare(a.name, b.name);
const bySize = (a: RenamerFile, b: RenamerFile) => a.length - b.length || byFolder(a, b);
const byModified = (a: RenamerFile, b: RenamerFile) =>
  Date.parse(a.lastModifiedUtc) - Date.parse(b.lastModifiedUtc) || byFolder(a, b);

/** Sorts that reorder the collection once; the order then stays as it is until changed again. */
export const sortOptions: readonly SortOption[] = [
  { value: 'name', label: 'Name (A to Z)', compare: byName },
  { value: 'name-desc', label: 'Name (Z to A)', compare: (a, b) => byName(b, a) },
  { value: 'folder', label: 'Folder, then name', compare: byFolder },
  { value: 'size', label: 'Size (smallest first)', compare: bySize },
  { value: 'size-desc', label: 'Size (largest first)', compare: (a, b) => bySize(b, a) },
  { value: 'modified', label: 'Modified (oldest first)', compare: byModified },
  { value: 'modified-desc', label: 'Modified (newest first)', compare: (a, b) => byModified(b, a) },
];

@Component({
  imports: [
    CdkDrag,
    CdkDragHandle,
    CdkDragPreview,
    CdkDropList,
    DatePipe,
    DecimalPipe,
    FaIconComponent,
    FolderPickerComponent,
  ],
  selector: 'app-file-selection',
  styleUrl: './file-selection.component.scss',
  templateUrl: './file-selection.component.html',
})
export class FileSelectionComponent {
  readonly files = input.required<RenamerFile[]>();
  readonly filesChanged = output<RenamerFile[]>();
  protected readonly trashIcon = faTrashCan;
  protected readonly folderIcon = faFolderOpen;
  protected readonly upIcon = faArrowUp;
  protected readonly downIcon = faArrowDown;
  protected readonly gripIcon = faGripVertical;
  protected readonly sortOptions = sortOptions;
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
  /** Collection positions by path; the order steps see as each file's position. */
  protected readonly positions = computed(
    () => new Map(this.files().map((file, index) => [file.path, index])),
  );
  /** The sort the collection is currently in, or empty for a custom order. */
  protected readonly currentSort = computed(() => {
    const files = this.files();
    return (
      sortOptions.find((option) =>
        files.every((file, i) => i === 0 || option.compare(files[i - 1], file) <= 0),
      )?.value ?? ''
    );
  });
  private readonly injector = inject(Injector);
  private readonly table = viewChild<ElementRef<HTMLElement>>('table');
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

  protected sort(value: string): void {
    const option = sortOptions.find((entry) => entry.value === value);
    if (!option) return;
    this.notice.set(`Sorted by ${option.label[0].toLowerCase()}${option.label.slice(1)}.`);
    this.filesChanged.emit([...this.files()].sort(option.compare));
  }

  /** Moves a file by `offset` places; only offered while the whole collection is shown. */
  protected move(path: string, offset: number): void {
    const from = this.positions().get(path);
    if (from === undefined) return;
    this.moveTo(from, from + offset, offset < 0 ? 'up' : 'down');
  }

  protected drop(event: CdkDragDrop<RenamerFile[]>): void {
    this.moveTo(event.previousIndex, event.currentIndex);
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

  private moveTo(from: number, to: number, focus?: 'up' | 'down'): void {
    const files = [...this.files()];
    if (this.filtering() || from === to || to < 0 || to >= files.length) return;
    const file = files[from];
    moveItemInArray(files, from, to);
    this.notice.set(`Moved ${file.name} to position ${to + 1} of ${files.length}.`);
    this.filesChanged.emit(files);
    if (!focus) return;
    // Keep focus on the moved file, switching direction once it reaches either end.
    const direction =
      (focus === 'up' && to === 0) || (focus === 'down' && to === files.length - 1)
        ? focus === 'up'
          ? 'down'
          : 'up'
        : focus;
    afterNextRender(
      () =>
        [
          ...(this.table()?.nativeElement.querySelectorAll<HTMLButtonElement>(
            'button[data-move]',
          ) ?? []),
        ]
          .find(
            (button) =>
              button.getAttribute('data-move') === direction &&
              button.getAttribute('data-path') === file.path,
          )
          ?.focus(),
      { injector: this.injector },
    );
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
