import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { FolderListing } from '../../folder-listing';
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

  it('counts the visible files directly in the folder', async () => {
    component.open();
    http.expectOne('/api/folders?path=').flush({
      ...root,
      files: [
        { name: 'a.mkv', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' },
        { name: 'b.mkv', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' },
        { name: '.DS_Store', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' },
      ],
    });
    await fixture.whenStable();
    expect(dialog.textContent).toContain('2 files in this folder.');
    expect(dialog.textContent).not.toContain('will add nothing');
    showHidden().click();
    fixture.detectChanges();
    expect(dialog.textContent).toContain('3 files in this folder.');
  });

  it('warns that a folder with only subfolders adds nothing until subfolders are included', async () => {
    component.open();
    http.expectOne('/api/folders?path=').flush({ ...root, files: [] });
    await fixture.whenStable();
    expect(dialog.textContent).toContain('0 files in this folder.');
    expect(dialog.textContent).toContain('selecting it will add nothing');
    includeSubfolders().click();
    fixture.detectChanges();
    expect(dialog.textContent).not.toContain('will add nothing');
  });

  it('does not warn about an empty folder without subfolders', async () => {
    component.open();
    http.expectOne('/api/folders?path=').flush({ ...root, directories: [], files: [] });
    await fixture.whenStable();
    expect(dialog.textContent).not.toContain('will add nothing');
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

  it('keeps folder hints in tooltips that Escape dismisses before the dialog', async () => {
    component.open();
    http
      .expectOne('/api/folders?path=')
      .flush({ ...root, files: [], directories: [{ name: 'Movies', path: 'Movies' }] });
    await fixture.whenStable();
    const trigger = dialog.querySelector('[aria-describedby="no-files-tip"]') as HTMLButtonElement;
    const host = trigger.parentElement as HTMLElement;
    const tip = dialog.querySelector('#no-files-tip') as HTMLElement;
    const isOpen = () => {
      fixture.detectChanges();
      return tip.classList.contains('tip-open');
    };
    expect(tip.getAttribute('role')).toBe('tooltip');
    expect(tip.textContent).toContain('selecting it will add nothing');
    expect(isOpen()).toBe(false);

    // Keyboard focus opens it, and Escape on the button closes only the tooltip.
    trigger.dispatchEvent(new FocusEvent('focus'));
    expect(isOpen()).toBe(true);
    const escapeKey = new KeyboardEvent('keydown', { key: 'Escape', cancelable: true });
    trigger.dispatchEvent(escapeKey);
    expect(isOpen()).toBe(false);
    expect(escapeKey.defaultPrevented).toBe(true);

    // With focus elsewhere, the dialog's own cancel (Escape) dismisses the tooltip first.
    host.dispatchEvent(new MouseEvent('mouseenter'));
    expect(isOpen()).toBe(true);
    const cancel = new Event('cancel', { cancelable: true });
    dialog.dispatchEvent(cancel);
    expect(isOpen()).toBe(false);
    expect(cancel.defaultPrevented).toBe(true);
    expect(dialog.open).toBe(true);

    // Staying hovered does not bring it back; leaving hides it after a short grace period.
    expect(isOpen()).toBe(false);
    trigger.click();
    expect(isOpen()).toBe(true);
    vi.useFakeTimers();
    host.dispatchEvent(new MouseEvent('mouseleave'));
    expect(isOpen()).toBe(true);
    vi.advanceTimersByTime(200);
    vi.useRealTimers();
    expect(isOpen()).toBe(false);

    // A cancel with no tooltip showing still closes the picker.
    dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(dialog.open).toBe(false);
    expect(dialog.querySelector('#browse-tip')?.textContent).toContain('Links are excluded');
  });

  it('renders folders in a scrollable table with selection actions in the footer', async () => {
    await openPicker();
    const region = dialog.querySelector('.folder-table');
    expect(region?.getAttribute('tabindex')).toBe('0');
    expect(region?.getAttribute('aria-labelledby')).toBe('subfolders-heading');
    expect(region?.querySelector('th')?.textContent).toBe('Subfolders');
    expect(region?.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(region?.querySelector('tbody button')?.textContent?.trim()).toBe('Movies');
    expect(region?.nextElementSibling).toBe(includeSubfolders().closest('label'));
    const actions = Array.from(dialog.querySelectorAll('footer button')).map((button) =>
      button.textContent?.trim(),
    );
    expect(actions).toEqual(['Cancel', 'Select folder']);
  });

  it('hides hidden and system entries unless the owner shows them', async () => {
    const show: FolderListing = {
      path: 'Show',
      parentPath: '',
      directories: [
        { name: '@eaDir', path: 'Show/@eaDir' },
        { name: 'S01', path: 'Show/S01' },
      ],
      files: [
        { name: '.DS_Store', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' },
        { name: 'Thumbs.db', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' },
      ],
    };
    const season: FolderListing = {
      path: 'Show/S01',
      parentPath: 'Show',
      directories: [{ name: '@eaDir', path: 'Show/S01/@eaDir' }],
      files: [{ name: 'E01.mkv', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' }],
    };
    const metadata: FolderListing = {
      path: 'Show/S01/@eaDir',
      parentPath: 'Show/S01',
      directories: [],
      files: [{ name: 'E01.mkv', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' }],
    };
    component.open();
    http.expectOne('/api/folders?path=').flush(show);
    await fixture.whenStable();
    const folders = () =>
      Array.from(dialog.querySelectorAll('.folder-table tbody button')).map((button) =>
        button.textContent?.trim(),
      );
    expect(folders()).toEqual(['S01']);
    showHidden().click();
    fixture.detectChanges();
    expect(folders()).toEqual(['@eaDir', 'S01']);
    showHidden().click();
    includeSubfolders().click();
    clickButton('Select folder');
    http
      .expectOne('/api/folders/selection?path=Show&includeSubfolders=true&includeHidden=false')
      .flush([show, season, metadata]);
    await fixture.whenStable();
    expect(selected).toEqual([[{ ...show, files: [] }, season]]);

    await openPickerAt(show);
    showHidden().click();
    clickButton('Select folder');
    http
      .expectOne('/api/folders/selection?path=Show&includeSubfolders=true&includeHidden=true')
      .flush([show, season, metadata]);
    await fixture.whenStable();
    expect(selected[1]).toEqual([show, season, metadata]);
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
      .expectOne('/api/folders/selection?path=&includeSubfolders=true&includeHidden=false')
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
      .expectOne('/api/folders/selection?path=&includeSubfolders=true&includeHidden=false')
      .flush([folders, movies, empty]);
    await fixture.whenStable();
    expect(selected).toEqual([[folders, movies, empty]]);
  });

  it('reports a subfolder error without emitting partial results and retries selection', async () => {
    await openPicker();
    includeSubfolders().click();
    clickButton('Select folder');
    http
      .expectOne('/api/folders/selection?path=&includeSubfolders=true&includeHidden=false')
      .flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    expect(dialog.open).toBe(true);
    expect(selected).toEqual([]);
    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain('No files were added.');
    expect(getButton('Select folder').disabled).toBe(true);

    clickButton('Try again');
    const movies: FolderListing = { ...root, path: 'Movies', parentPath: '', directories: [] };
    http
      .expectOne('/api/folders/selection?path=&includeSubfolders=true&includeHidden=false')
      .flush([root, movies]);
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
      const request = http.expectOne(
        '/api/folders/selection?path=&includeSubfolders=true&includeHidden=false',
      );
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
    const request = http.expectOne(
      '/api/folders/selection?path=&includeSubfolders=true&includeHidden=false',
    );
    fixture.destroy();
    expect(request.cancelled).toBe(true);
    expect(selected).toEqual([]);
  });

  function includeSubfolders(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  function showHidden(): HTMLInputElement {
    return fixture.nativeElement.querySelectorAll('input[type="checkbox"]')[1];
  }

  async function openPickerAt(folder: FolderListing): Promise<void> {
    component.open();
    http.expectOne(`/api/folders?path=${folder.path}`).flush(folder);
    await fixture.whenStable();
  }

  function confirmFolder(folder: FolderListing): void {
    clickButton('Select folder');
    http
      .expectOne(
        `/api/folders/selection?path=${folder.path}&includeSubfolders=false&includeHidden=false`,
      )
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
