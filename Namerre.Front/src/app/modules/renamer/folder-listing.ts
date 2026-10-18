export interface FolderListing {
  path: string;
  parentPath: string | null;
  directories: { name: string; path: string }[];
  files: { name: string; length: number; lastModifiedUtc: string }[];
}
