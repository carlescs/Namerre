import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { FolderListing } from '../../folder-listing';

@Component({
  selector: 'app-renamer',
  imports: [DatePipe, DecimalPipe],
  styleUrl: './renamer.component.scss',
  templateUrl: './renamer.component.html',
})
export class RenamerComponent {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private requestedPath = '';

  protected readonly folder = signal<FolderListing | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  constructor() {
    this.openFolder('');
  }

  protected openFolder(path: string): void {
    if (this.loading()) {
      return;
    }

    this.requestedPath = path;
    this.error.set('');
    this.loading.set(true);
    this.http
      .get<FolderListing>('/api/folders', { params: { path } })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (folder) => this.folder.set(folder),
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
