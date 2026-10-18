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
  let selected: FolderListing[];
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
    expect(selected).toEqual([]);
    clickButton('Select folder');
    expect(dialog.open).toBe(false);
    expect(selected).toEqual([root]);
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
    clickButton('Select folder');
    expect(selected).toEqual([movies]);
  });

  it('cancels browsing without emitting and reopens at the last confirmed folder', async () => {
    await openPicker();
    clickButton('Select folder');
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
    expect(selected).toEqual([root]);
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
    clickButton('Select folder');
    component.open();
    http.expectOne('/api/folders?path=Movies').flush(movies);
    await fixture.whenStable();
    expect(dialog.textContent).toContain('Current folder: Movies');
    expect(selected).toEqual([movies]);
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
