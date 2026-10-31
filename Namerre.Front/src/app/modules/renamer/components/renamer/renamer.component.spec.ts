import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { workflowModeStorageKey } from '../../../../workflow-mode/workflow-mode';
import type { FolderListing } from '../../folder-listing';
import { FolderPickerComponent } from '../folder-picker/folder-picker.component';
import { RenamerComponent } from './renamer.component';

describe('RenamerComponent', () => {
  let fixture: ComponentFixture<RenamerComponent>;
  let picker: FolderPickerComponent;
  let http: HttpTestingController;
  const root: FolderListing = {
    path: '',
    parentPath: null,
    directories: [{ name: 'Movies', path: 'Movies' }],
    files: [{ name: 'notes.txt', length: 12, lastModifiedUtc: '2026-10-18T07:00:00Z' }],
  };

  beforeEach(async () => {
    localStorage.clear();
    // These specs cover the Advanced stages; Simple mode has its own.
    localStorage.setItem(workflowModeStorageKey, 'advanced');
    await TestBed.configureTestingModule({
      imports: [RenamerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(RenamerComponent);
    http = TestBed.inject(HttpTestingController);
    await fixture.whenStable();
    picker = fixture.debugElement
      .query(By.directive(FolderPickerComponent))
      .injector.get(FolderPickerComponent);
  });

  afterEach(() => {
    fixture.destroy();
    localStorage.clear();
    http.verify();
  });

  it('restores the collection saved in this browser and keeps it in sync', async () => {
    await select(root);
    expect(JSON.parse(localStorage.getItem('namerre.draft.files.v1') ?? '[]')).toHaveLength(1);
    const restored = TestBed.createComponent(RenamerComponent);
    await restored.whenStable();
    const element: HTMLElement = restored.nativeElement;
    expect(element.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(element.textContent).toContain('Restored 1 file from your last visit');
    restored.destroy();
    clearButton().click();
    await fixture.whenStable();
    expect(localStorage.getItem('namerre.draft.files.v1')).toBeNull();
  });

  it('starts with an empty renaming collection', () => {
    expect(main().textContent).toContain('Files for renaming (0)');
    expect(main().textContent).toContain('No files added yet.');
    expect(rows()).toHaveLength(0);
    expect(nextButton().disabled).toBe(true);
    expect(main().querySelector('.stage-header h2')?.textContent).toBe('Select files');
    expect(main().querySelector('app-plugin-editor')).toBeNull();
    http.expectNone('/api/rename-plugins');
  });

  it('takes selected files into the wizard and preserves them when returning to the first stage', async () => {
    await select(root);
    expect(nextButton().disabled).toBe(false);
    nextButton().click();
    await fixture.whenStable();
    expect(main().querySelector('.stage-header h2')?.textContent).toBe('Configure plugins');
    expect(
      main().querySelector('[aria-labelledby="files-heading"]')?.closest('[hidden]'),
    ).not.toBeNull();
    http.expectOne('/api/rename-plugins').flush([]);
    await fixture.whenStable();
    http.expectOne('/api/rename-presets').flush([]);
    const back = main().querySelector(
      '[aria-label="Wizard navigation"] button',
    ) as HTMLButtonElement;
    back.click();
    await fixture.whenStable();
    expect(main().querySelector('.stage-header h2')?.textContent).toBe('Select files');
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('notes.txt');
    clearButton().click();
    await fixture.whenStable();
    expect(nextButton().disabled).toBe(true);
  });

  it('blocks advancing a collection larger than 200 files', async () => {
    await select({
      ...root,
      files: Array.from({ length: 201 }, (_, index) => ({
        ...root.files[0],
        name: `file-${index}.txt`,
      })),
    });
    expect(nextButton().disabled).toBe(true);
    expect(main().textContent).toContain('at most 200 files');
  });

  it('opens the dedicated folder picker', () => {
    const open = vi.spyOn(picker, 'open').mockImplementation(() => {});
    main().querySelector('button')?.click();
    expect(open).toHaveBeenCalledOnce();
  });

  it('adds confirmed files and displays their source folder', async () => {
    await select(root);
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('notes.txt');
    expect(rows()[0].textContent).toContain('Server root');
    expect(main().textContent).toContain('Files for renaming (1)');
  });

  it('accumulates files from different folders even when their names match', async () => {
    await select(root);
    await select({ ...root, path: 'Movies', parentPath: '' });
    expect(rows()).toHaveLength(2);
    expect(rows()[0].textContent).toContain('Server root');
    expect(rows()[1].textContent).toContain('Movies');
    expect(main().textContent).toContain('Files for renaming (2)');
  });

  it('skips existing paths while adding newly discovered files', async () => {
    await select(root);
    await select(root);
    expect(rows()).toHaveLength(1);
    await select({
      ...root,
      files: [
        ...root.files,
        { name: 'new.txt', length: 3, lastModifiedUtc: '2026-10-18T08:00:00Z' },
      ],
    });
    expect(rows()).toHaveLength(2);
    expect(rows()[1].textContent).toContain('new.txt');
  });

  it('adds a recursive selection with distinct paths and skips files already in the collection', async () => {
    await select(root);
    picker.folderSelected.emit([
      root,
      { ...root, path: 'Movies', parentPath: '' },
      { ...root, path: 'Movies/Classics', parentPath: 'Movies' },
    ]);
    await fixture.whenStable();
    expect(rows()).toHaveLength(3);
    expect(rows()[0].textContent).toContain('Server root');
    expect(rows()[1].textContent).toContain('Movies');
    expect(rows()[2].textContent).toContain('Movies/Classics');
    expect(main().textContent).toContain('Files for renaming (3)');
  });

  it('does not clear existing files when an empty folder is selected', async () => {
    await select(root);
    await select({ path: 'Empty', parentPath: '', directories: [], files: [] });
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('notes.txt');
  });

  it('disables clearing an empty collection', () => {
    expect(clearButton().disabled).toBe(true);
  });

  it('clears the collection and allows adding the same files again', async () => {
    await select(root);
    await select({ ...root, path: 'Movies', parentPath: '' });
    expect(clearButton().disabled).toBe(false);
    clearButton().click();
    await fixture.whenStable();
    expect(rows()).toHaveLength(0);
    expect(main().textContent).toContain('Files for renaming (0)');
    expect(main().textContent).toContain('No files added yet.');
    expect(clearButton().disabled).toBe(true);
    await select(root);
    expect(rows()).toHaveLength(1);
  });

  it('removes only the selected path when filenames match across folders', async () => {
    await select(root);
    await select({ ...root, path: 'Movies', parentPath: '' });
    const remove = rows()[1].querySelector<HTMLButtonElement>('button[aria-label^="Remove"]');
    expect(remove?.getAttribute('aria-label')).toBe('Remove Movies/notes.txt from collection');
    expect(remove?.textContent?.trim()).toBe('');
    expect(remove?.querySelector('svg')?.getAttribute('data-icon')).toBe('trash-can');
    expect(remove?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    remove?.click();
    await fixture.whenStable();
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('Server root');
    expect(main().textContent).toContain('Files for renaming (1)');
    await select({ ...root, path: 'Movies', parentPath: '' });
    expect(rows()).toHaveLength(2);
  });

  it('shows the empty collection state after removing the final item', async () => {
    await select(root);
    rows()[0].querySelector<HTMLButtonElement>('button[aria-label^="Remove"]')?.click();
    await fixture.whenStable();
    expect(rows()).toHaveLength(0);
    expect(main().textContent).toContain('No files added yet.');
    expect(clearButton().disabled).toBe(true);
  });

  it('leaves the collection unchanged when the picker is cancelled', async () => {
    await select(root);
    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    dialog.showModal = vi.fn(() => dialog.setAttribute('open', ''));
    dialog.close = vi.fn(() => dialog.removeAttribute('open'));
    main().querySelector('button')?.click();
    const request = http.expectOne('/api/folders?path=');
    dialog.dispatchEvent(new Event('cancel'));
    await fixture.whenStable();
    expect(request.cancelled).toBe(true);
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('notes.txt');
  });

  it('filters by filename or folder without changing the selected collection', async () => {
    await select(root);
    await select({ ...root, path: 'Movies', parentPath: '' });
    await filter('  MOVIES  ');
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('Movies');
    expect(main().textContent).toContain('Files for renaming (2)');
    expect(main().textContent).toContain('Showing 1 of 2 files');
    expect(nextButton().disabled).toBe(false);
    await filter('notes');
    expect(rows()).toHaveLength(2);
    await filter('not found');
    expect(rows()).toHaveLength(0);
    expect(main().textContent).toContain('No files match your filter.');
    expect(nextButton().disabled).toBe(false);
    const clearFilter = Array.from(main().querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Clear filter',
    );
    clearFilter?.click();
    await fixture.whenStable();
    expect(rows()).toHaveLength(2);
  });

  it('removes only a filtered file and keeps files hidden by the filter', async () => {
    await select(root);
    await select({ ...root, path: 'Movies', parentPath: '' });
    await filter('movies');
    rows()[0].querySelector<HTMLButtonElement>('button[aria-label^="Remove"]')?.click();
    await fixture.whenStable();
    expect(main().textContent).toContain('Files for renaming (1)');
    expect(rows()).toHaveLength(0);
    await filter('');
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('Server root');
  });

  it('resets the filter when adding folders or clearing the whole collection', async () => {
    await select(root);
    await filter('missing');
    await select({ ...root, path: 'Movies', parentPath: '' });
    expect(rows()).toHaveLength(2);
    expect((main().querySelector('#file-search') as HTMLInputElement).value).toBe('');
    await filter('movies');
    clearButton().click();
    await fixture.whenStable();
    expect(rows()).toHaveLength(0);
    expect(nextButton().disabled).toBe(true);
    await select(root);
    expect(rows()).toHaveLength(1);
    expect((main().querySelector('#file-search') as HTMLInputElement).value).toBe('');
  });

  async function filter(value: string): Promise<void> {
    const search = main().querySelector('#file-search') as HTMLInputElement;
    search.value = value;
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function select(folder: FolderListing): Promise<void> {
    picker.folderSelected.emit([folder]);
    await fixture.whenStable();
  }

  function main(): HTMLElement {
    return fixture.nativeElement.querySelector('main');
  }

  function clearButton(): HTMLButtonElement {
    const button = Array.from(main().querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === 'Clear collection',
    );
    if (!button) {
      throw new Error('Clear collection button not found');
    }
    return button;
  }

  function nextButton(): HTMLButtonElement {
    return main().querySelector(
      '[aria-label="Wizard navigation"] button:last-child',
    ) as HTMLButtonElement;
  }

  function rows(): NodeListOf<HTMLTableRowElement> {
    return main().querySelectorAll('tbody tr');
  }
});
