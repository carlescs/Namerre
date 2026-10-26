import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCircleInfo } from '@fortawesome/free-solid-svg-icons';
import { finalize, Subscription } from 'rxjs';
import { FolderListing } from '../../folder-listing';
import { isHiddenEntry, isInsideHiddenFolder } from '../../hidden-entries';

@Component({
  imports: [FaIconComponent],
  selector: 'app-folder-picker',
  styleUrl: './folder-picker.component.scss',
  templateUrl: './folder-picker.component.html',
})
export class FolderPickerComponent {
  readonly folderSelected = output<FolderListing[]>();

  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly picker = viewChild.required<ElementRef<HTMLDialogElement>>('folderPicker');
  private readonly currentPath = viewChild.required<ElementRef<HTMLElement>>('currentPath');
  private request?: Subscription;
  private requestedPath = '';
  private selectedFolder: FolderListing | null = null;
  private retrySelection = false;

  protected readonly browsingFolder = signal<FolderListing | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly includeSubfolders = signal(false);
  protected readonly showHidden = signal(false);
  protected readonly infoIcon = faCircleInfo;
  /** Tooltip opened by a click or tap; hover and keyboard focus open them through CSS. */
  protected readonly openTip = signal<string | null>(null);
  private tipTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.tipTimer));
  }
  protected readonly directories = computed(() => {
    const directories = this.browsingFolder()?.directories ?? [];
    return this.showHidden()
      ? directories
      : directories.filter((directory) => !isHiddenEntry(directory.name));
  });
  protected readonly fileCount = computed(() => {
    const files = this.browsingFolder()?.files ?? [];
    return this.showHidden()
      ? files.length
      : files.filter((file) => !isHiddenEntry(file.name)).length;
  });
  protected readonly selecting = signal(false);

  open(): void {
    this.browsingFolder.set(this.selectedFolder);
    this.picker().nativeElement.showModal();
    this.openFolder(this.selectedFolder?.path ?? '');
  }

  /** Shows a tooltip on hover, keyboard focus or a click/tap; the component alone decides. */
  protected showTip(tip: string): void {
    clearTimeout(this.tipTimer);
    this.openTip.set(tip);
  }

  /** Hides a tooltip on blur, or shortly after the pointer leaves so it can reach the bubble. */
  protected hideTip(tip: string, delayed = false): void {
    clearTimeout(this.tipTimer);
    const hide = () => {
      if (this.openTip() === tip) this.openTip.set(null);
    };
    if (delayed) this.tipTimer = setTimeout(hide, 150);
    else hide();
  }

  protected closeTip(event: Event): void {
    if (!this.openTip()) return;
    // Escape closes an open tooltip first, without also closing the dialog.
    event.preventDefault();
    event.stopPropagation();
    clearTimeout(this.tipTimer);
    this.openTip.set(null);
  }

  /** The dialog's own Escape handling: an open tooltip is dismissed before the picker closes. */
  protected onCancel(event: Event): void {
    if (this.openTip()) {
      this.closeTip(event);
      return;
    }
    this.cancelPicker();
  }

  protected cancelPicker(): void {
    this.request?.unsubscribe();
    this.picker().nativeElement.close();
  }

  protected selectFolder(): void {
    const selected = this.browsingFolder();
    if (!selected || this.loading() || this.error()) {
      return;
    }

    this.retrySelection = true;
    this.loading.set(true);
    this.selecting.set(true);
    this.request = this.http
      .get<FolderListing[]>('/api/folders/selection', {
        params: {
          path: selected.path,
          includeSubfolders: this.includeSubfolders(),
          includeHidden: this.showHidden(),
        },
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.loading.set(false);
          this.selecting.set(false);
        }),
      )
      .subscribe({
        next: (folders) => this.confirmSelection(selected, folders),
        error: (error: HttpErrorResponse) => this.showError(error),
      });
  }

  protected openFolder(path: string): void {
    if (this.loading()) {
      return;
    }

    this.requestedPath = path;
    this.retrySelection = false;
    this.openTip.set(null);
    this.error.set('');
    this.loading.set(true);
    this.request = this.http
      .get<FolderListing>('/api/folders', { params: { path } })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (folder) => {
          this.browsingFolder.set(folder);
          this.currentPath().nativeElement.focus();
        },
        error: (error: HttpErrorResponse) => this.showError(error),
      });
  }

  protected retry(): void {
    if (this.retrySelection) {
      this.error.set('');
      this.selectFolder();
    } else {
      this.openFolder(this.requestedPath);
    }
  }

  private confirmSelection(selected: FolderListing, folders: FolderListing[]): void {
    this.selectedFolder = selected;
    this.picker().nativeElement.close();
    this.folderSelected.emit(
      this.showHidden()
        ? folders
        : folders
            .filter((folder) => !isInsideHiddenFolder(folder.path, selected.path))
            .map((folder) => ({
              ...folder,
              files: folder.files.filter((file) => !isHiddenEntry(file.name)),
            })),
    );
  }

  private showError(error: HttpErrorResponse): void {
    const messages: Record<number, string> = {
      0: 'Cannot reach the server. Check your connection and try again.',
      400: 'Choose a folder within the configured server root.',
      403: 'The server cannot access this folder. Links and junctions cannot be browsed.',
      404: 'This folder no longer exists. Select another folder.',
      503: 'Server folder browsing is unavailable. Check FileBrowser:RootPath and server permissions.',
    };
    const message = messages[error.status] ?? 'The folder could not be loaded. Try again.';
    this.error.set(this.retrySelection ? `No files were added. ${message}` : message);
  }
}
