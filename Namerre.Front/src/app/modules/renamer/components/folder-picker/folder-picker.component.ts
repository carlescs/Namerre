import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, Subscription } from 'rxjs';
import { FolderListing } from '../../folder-listing';

@Component({
  selector: 'app-folder-picker',
  styleUrl: './folder-picker.component.scss',
  templateUrl: './folder-picker.component.html',
})
export class FolderPickerComponent {
  readonly folderSelected = output<FolderListing>();

  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly picker = viewChild.required<ElementRef<HTMLDialogElement>>('folderPicker');
  private readonly currentPath = viewChild.required<ElementRef<HTMLElement>>('currentPath');
  private request?: Subscription;
  private requestedPath = '';
  private selectedFolder: FolderListing | null = null;

  protected readonly browsingFolder = signal<FolderListing | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  open(): void {
    this.browsingFolder.set(this.selectedFolder);
    this.picker().nativeElement.showModal();
    this.openFolder(this.selectedFolder?.path ?? '');
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

    this.selectedFolder = selected;
    this.picker().nativeElement.close();
    this.folderSelected.emit(selected);
  }

  protected openFolder(path: string): void {
    if (this.loading()) {
      return;
    }

    this.requestedPath = path;
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
        error: (error: HttpErrorResponse) => {
          const messages: Record<number, string> = {
            0: 'Cannot reach the server. Check your connection and try again.',
            400: 'Choose a folder within the configured server root.',
            403: 'The server cannot access this folder. Links and junctions cannot be browsed.',
            404: 'This folder no longer exists. Select another folder.',
            503: 'Server folder browsing is unavailable. Check FileBrowser:RootPath and server permissions.',
          };
          this.error.set(messages[error.status] ?? 'The folder could not be loaded. Try again.');
        },
      });
  }

  protected retry(): void {
    this.openFolder(this.requestedPath);
  }
}
