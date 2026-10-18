import { FileEntry } from './folder-listing';

export interface RenamerFile extends FileEntry {
  path: string;
  folderPath: string;
}
