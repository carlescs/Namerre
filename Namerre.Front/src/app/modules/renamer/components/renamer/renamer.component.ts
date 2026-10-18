import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, signal } from '@angular/core';
import { FolderListing } from '../../folder-listing';
import { RenamerFile } from '../../renamer-file';
import { FolderPickerComponent } from '../folder-picker/folder-picker.component';

@Component({
  selector: 'app-renamer',
  imports: [DatePipe, DecimalPipe, FolderPickerComponent],
  styleUrl: './renamer.component.scss',
  templateUrl: './renamer.component.html',
})
export class RenamerComponent {
  protected readonly files = signal<RenamerFile[]>([]);

  protected clearFiles(): void {
    this.files.set([]);
  }

  protected removeFile(path: string): void {
    this.files.update((files) => files.filter((file) => file.path !== path));
  }

  protected addFolder(folder: FolderListing): void {
    this.files.update((files) => {
      const paths = new Set(files.map((file) => file.path));
      const additions: RenamerFile[] = [];
      for (const file of folder.files) {
        const path = folder.path ? `${folder.path}/${file.name}` : file.name;
        if (!paths.has(path)) {
          paths.add(path);
          additions.push({ ...file, path, folderPath: folder.path });
        }
      }
      return [...files, ...additions];
    });
  }
}
