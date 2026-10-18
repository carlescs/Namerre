import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FolderListing } from '../../folder-listing';
import { FolderPickerComponent } from './folder-picker.component';

describe('FolderPickerComponent', () => {
  let component: FolderPickerComponent;
  let fixture: ComponentFixture<FolderPickerComponent>;
  let http: HttpTestingController;
  let dialog: HTMLDialogElement;
  let selected: FolderListing[][];
  const root: FolderListing = {
    path: '',
    parentPath: null,
    directories: [{ name: 'Movies', path: 'Movies' }],
    files: [{ name: 'notes.txt', length: 12, lastModifiedUtc: '2026-10-18T07:00:00Z' }],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FolderPickerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(FolderPickerComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    selected = [];
    component.folderSelected.subscribe((folder) => selected.push(folder));
    await fixture.whenStable();
    dialog = fixture.nativeElement.querySelector('dialog');
    // jsdom does not implement the native dialog methods.
    dialog.showModal = vi.fn(() => dialog.setAttribute('open', ''));
    dialog.close = vi.fn(() => dialog.removeAttribute('open'));
  });

  afterEach(() => http.verify());

  it('opens the modal and only emits the folder after confirmation', async () => {
    expect(dialog.open).toBe(false);
    await openPicker();
    expect(dialog.open).toBe(true);
    expect(dialog.textContent).toContain('Current folder: Server root');
    expect(includeSubfolders().checked).toBe(false);
    expect(selected).toEqual([]);
    confirmFolder(root);
    expect(dialog.open).toBe(false);
    expect(selected).toEqual([[root]]);
  });

  it('navigates to subfolders and back to the parent without emitting a selection', async () => {
    await openPicker();
    clickButton('Movies');
    expect(dialog.textContent).toContain('Loading folder...');
    http.expectOne('/api/folders?path=Movies').flush({
      path: 'Movies',
      parentPath: '',
      directories: [],
      files: [],
    });
    await fixture.whenStable();
    expect(dialog.textContent).toContain('Current folder: Movies');
    expect(dialog.textContent).toContain('No subfolders in this folder.');
    clickButton('Parent folder');
    http.expectOne('/api/folders?path=').flush(root);
    await fixture.whenStable();
    expect(dialog.textContent).toContain('Current folder: Server root');
    expect(selected).toEqual([]);
  });

  it('renders folders in a scrollable table with selection actions in the footer', async () => {
    await openPicker();
    const region = dialog.querySelector('.folder-table');
    expect(region?.getAttribute('tabindex')).toBe('0');
    expect(region?.getAttribute('aria-labelledby')).toBe('subfolders-heading');
    expect(region?.querySelector('th')?.textContent).toBe('Folder name');
    expect(region?.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(region?.querySelector('tbody button')?.textContent?.trim()).toBe('Movies');
    expect(region?.nextElementSibling).toBe(includeSubfolders().closest('label'));
    const actions = Array.from(dialog.querySelectorAll('footer button')).map((button) =>
      button.textContent?.trim(),
    );
    expect(actions).toEqual(['Cancel', 'Select folder']);
  });

  it('disables selection on errors and supports retry', async () => {
    await openPicker();
    clickButton('Movies');
    http.expectOne('/api/folders?path=Movies').flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain(
      'The server cannot access this folder.',
    );
    expect(getButton('Select folder').disabled).toBe(true);
    expect(selected).toEqual([]);
    clickButton('Try again');
    const movies: FolderListing = {
      path: 'Movies',
      parentPath: '',
      directories: [],
      files: [],
    };
    http.expectOne('/api/folders?path=Movies').flush(movies);
    await fixture.whenStable();
    expect(dialog.querySelector('[role="alert"]')).toBeNull();
    confirmFolder(movies);
    expect(selected).toEqual([[movies]]);
  });

  it('cancels browsing without emitting and reopens at the last confirmed folder', async () => {
    await openPicker();
    confirmFolder(root);
    await openPicker();
    clickButton('Movies');
    http.expectOne('/api/folders?path=Movies').flush({
      path: 'Movies',
      parentPath: '',
      directories: [],
      files: [],
    });
    await fixture.whenStable();
    clickButton('Cancel');
    expect(dialog.open).toBe(false);
    expect(selected).toEqual([[root]]);
    await openPicker();
    expect(dialog.textContent).toContain('Current folder: Server root');
  });

  it('reopens at the last confirmed subfolder', async () => {
    await openPicker();
    clickButton('Movies');
    const movies: FolderListing = {
      path: 'Movies',
      parentPath: '',
      directories: [],
      files: [],
    };
    http.expectOne('/api/folders?path=Movies').flush(movies);
    await fixture.whenStable();
    confirmFolder(movies);
    component.open();
    http.expectOne('/api/folders?path=Movies').flush(movies);
    await fixture.whenStable();
    expect(dialog.textContent).toContain('Current folder: Movies');
    expect(selected).toEqual([[movies]]);
  });

  it('cancels pending navigation on Escape and can reopen the picker', async () => {
    component.open();
    fixture.detectChanges();
    const request = http.expectOne('/api/folders?path=');
    expect(getButton('Select folder').disabled).toBe(true);
    dialog.dispatchEvent(new Event('cancel'));
    fixture.detectChanges();
    expect(request.cancelled).toBe(true);
    expect(dialog.open).toBe(false);
    expect(selected).toEqual([]);
    await openPicker();
    expect(dialog.open).toBe(true);
    expect(getButton('Select folder').disabled).toBe(false);
  });

  it('requests all descendant folders in a single backend selection request', async () => {
    await openPicker();
    includeSubfolders().click();
    clickButton('Select folder');
    expect(dialog.open).toBe(true);
    expect(selected).toEqual([]);
    expect(includeSubfolders().disabled).toBe(true);
    expect(getButton('Select folder').disabled).toBe(true);
    expect(dialog.textContent).toContain('Loading files from subfolders...');

    const movies: FolderListing = {
      ...root,
      path: 'Movies',
      parentPath: '',
      directories: [{ name: 'Classics', path: 'Movies/Classics' }],
    };
    const classics: FolderListing = {
      ...root,
      path: 'Movies/Classics',
      parentPath: 'Movies',
      directories: [],
    };
    http.expectNone('/api/folders?path=Movies');
    http
      .expectOne('/api/folders/selection?path=&includeSubfolders=true')
      .flush([root, movies, classics]);
    await fixture.whenStable();
    expect(selected).toEqual([[root, movies, classics]]);
    expect(dialog.open).toBe(false);
  });

  it('includes sibling branches and empty folders', async () => {
    const folders: FolderListing = {
      ...root,
      directories: [...root.directories, { name: 'Empty', path: 'Empty' }],
    };
    component.open();
    http.expectOne('/api/folders?path=').flush(folders);
    await fixture.whenStable();
    includeSubfolders().click();
    clickButton('Select folder');
    const movies: FolderListing = { ...root, path: 'Movies', parentPath: '', directories: [] };
    const empty: FolderListing = {
      path: 'Empty',
      parentPath: '',
      directories: [],
      files: [],
    };
    http
      .expectOne('/api/folders/selection?path=&includeSubfolders=true')
      .flush([folders, movies, empty]);
    await fixture.whenStable();
    expect(selected).toEqual([[folders, movies, empty]]);
  });

  it('reports a subfolder error without emitting partial results and retries selection', async () => {
    await openPicker();
    includeSubfolders().click();
    clickButton('Select folder');
    http
      .expectOne('/api/folders/selection?path=&includeSubfolders=true')
      .flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    expect(dialog.open).toBe(true);
    expect(selected).toEqual([]);
    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain('No files were added.');
    expect(getButton('Select folder').disabled).toBe(true);

    clickButton('Try again');
    const movies: FolderListing = { ...root, path: 'Movies', parentPath: '', directories: [] };
    http.expectOne('/api/folders/selection?path=&includeSubfolders=true').flush([root, movies]);
    await fixture.whenStable();
    expect(selected).toEqual([[root, movies]]);
    expect(dialog.open).toBe(false);
  });

  it.each(['Cancel', 'Escape'])(
    'cancels recursive loading with %s without adding files',
    async (action) => {
      await openPicker();
      includeSubfolders().click();
      clickButton('Select folder');
      const request = http.expectOne('/api/folders/selection?path=&includeSubfolders=true');
      if (action === 'Cancel') {
        clickButton('Cancel');
      } else {
        dialog.dispatchEvent(new Event('cancel'));
      }
      await fixture.whenStable();
      expect(request.cancelled).toBe(true);
      expect(selected).toEqual([]);
      expect(dialog.open).toBe(false);
      await openPicker();
      expect(getButton('Select folder').disabled).toBe(false);
    },
  );

  it('cancels recursive loading when destroyed without emitting a selection', async () => {
    await openPicker();
    includeSubfolders().click();
    clickButton('Select folder');
    const request = http.expectOne('/api/folders/selection?path=&includeSubfolders=true');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
    expect(selected).toEqual([]);
  });

  function includeSubfolders(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  function confirmFolder(folder: FolderListing): void {
    clickButton('Select folder');
    http
      .expectOne(`/api/folders/selection?path=${folder.path}&includeSubfolders=false`)
      .flush([folder]);
  }

  async function openPicker(): Promise<void> {
    component.open();
    http.expectOne('/api/folders?path=').flush(root);
    await fixture.whenStable();
  }

  function getButton(label: string): HTMLButtonElement {
    const button = Array.from(dialog.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    if (!button) {
      throw new Error(`Button not found: ${label}`);
    }
    return button;
  }

  function clickButton(label: string): void {
    getButton(label).click();
    fixture.detectChanges();
  }
});
