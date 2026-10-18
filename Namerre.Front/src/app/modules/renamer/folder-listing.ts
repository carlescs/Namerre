export interface FolderListing {
  path: string;
  parentPath: string | null;
  directories: { name: string; path: string }[];
  files: FileEntry[];
}

export interface FileEntry {
  name: string;
  length: number;
  lastModifiedUtc: string;
}
