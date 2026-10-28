import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import type { Mock } from 'vitest';
import type { RenamerFile } from '../../renamer-file';
import { FolderPickerComponent } from '../folder-picker/folder-picker.component';
import { FileSelectionComponent } from './file-selection.component';

describe('FileSelectionComponent', () => {
  it('emits deduplicated collections without mutating its input', async () => {
    await TestBed.configureTestingModule({
      imports: [FileSelectionComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(FileSelectionComponent);
    const file: RenamerFile = {
      name: 'a.mkv',
      path: 'a.mkv',
      folderPath: '',
      length: 1,
      lastModifiedUtc: '2026-10-18T00:00:00Z',
    };
    const files = [file];
    fixture.componentRef.setInput('files', files);
    const changed = vi.fn<(files: RenamerFile[]) => void>();
    fixture.componentInstance.filesChanged.subscribe(changed);
    await fixture.whenStable();
    const picker = fixture.debugElement.query(By.directive(FolderPickerComponent))
      .componentInstance as FolderPickerComponent;
    picker.folderSelected.emit([
      {
        path: '',
        parentPath: null,
        directories: [],
        files: [file, { ...file, name: 'b.mkv' }],
      },
    ]);
    expect(changed).toHaveBeenCalledWith([file, { ...file, name: 'b.mkv', path: 'b.mkv' }]);
    expect(files).toEqual([file]);
    fixture.nativeElement.querySelector('.collection-actions button:nth-child(2)').click();
    expect(changed).toHaveBeenLastCalledWith([]);
    fixture.destroy();
    TestBed.inject(HttpTestingController).verify();
  });

  describe('with a mixed collection', () => {
    let fixture: ComponentFixture<FileSelectionComponent>;
    let changed: Mock<(files: RenamerFile[]) => void>;
    const make = (name: string, folderPath = 'Show/S01', length = 304_311_922): RenamerFile => ({
      name,
      path: `${folderPath}/${name}`,
      folderPath,
      length,
      lastModifiedUtc: '2026-10-18T07:00:00Z',
    });
    const videos = [make('e1.mp4'), make('e2.mp4'), make('e3.MP4')];
    const thumbs = [make('e1-thumb.jpg'), make('e2-thumb.jpg')];

    beforeEach(async () => {
      await TestBed.configureTestingModule({
        imports: [FileSelectionComponent],
        providers: [provideHttpClient(), provideHttpClientTesting()],
      }).compileComponents();
      fixture = TestBed.createComponent(FileSelectionComponent);
      fixture.componentRef.setInput('files', [...videos, ...thumbs]);
      changed = vi.fn<(files: RenamerFile[]) => void>();
      fixture.componentInstance.filesChanged.subscribe(changed);
      await fixture.whenStable();
    });

    afterEach(() => fixture.destroy());

    const el = (): HTMLElement => fixture.nativeElement;
    const rows = () => el().querySelectorAll('tbody tr');
    const squash = (text: string | null | undefined) => (text ?? '').replace(/\s+/g, ' ').trim();
    const button = (label: string): HTMLButtonElement | undefined =>
      Array.from(el().querySelectorAll('button')).find((b) =>
        squash(b.textContent).startsWith(label),
      );
    const chip = (label: string) => button(label) as HTMLButtonElement;
    const typeText = (value: string) => {
      const search = el().querySelector('#file-search') as HTMLInputElement;
      search.value = value;
      search.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    it('shows human-readable sizes with the exact bytes in a title', () => {
      expect(el().querySelector('th:nth-child(2)')?.textContent?.trim()).toBe('Size');
      const cell = rows()[0].querySelector('td:nth-child(2)') as HTMLElement;
      expect(cell.textContent?.trim()).toBe('290 MB');
      expect(cell.getAttribute('title')).toBe('304,311,922 bytes');
    });

    it('lists file types with counts, case-insensitively', () => {
      const chips = Array.from(el().querySelectorAll('.type-chip')).map((c) =>
        squash(c.textContent),
      );
      expect(chips).toEqual(['mp4 · 3', 'jpg · 2']);
    });

    it('filters by type and removes exactly those files', () => {
      chip('jpg').click();
      fixture.detectChanges();
      expect(rows()).toHaveLength(2);
      expect(chip('jpg').getAttribute('aria-pressed')).toBe('true');
      button('Remove these 2 files')!.click();
      expect(changed).toHaveBeenCalledWith(videos);
    });

    it('toggles a type chip off again', () => {
      chip('jpg').click();
      fixture.detectChanges();
      chip('jpg').click();
      fixture.detectChanges();
      expect(rows()).toHaveLength(5);
      expect(button('Remove these')).toBeUndefined();
    });

    it('removes only the files shown by the text filter', () => {
      typeText('e1');
      button('Remove 2 shown files')!.click();
      expect(changed).toHaveBeenCalledWith([videos[1], videos[2], thumbs[1]]);
    });

    it('combines the type and text filters and never removes hidden files', () => {
      chip('mp4').click();
      typeText('e2');
      expect(rows()).toHaveLength(1);
      button('Remove these 1 file')!.click();
      expect(changed).toHaveBeenCalledWith([videos[0], videos[2], ...thumbs]);
    });

    it('offers no bulk removal without a filter', () => {
      expect(button('Remove')).toBeUndefined();
      expect(button('Clear collection')).toBeDefined();
    });

    it('announces what a folder added and what was already present', async () => {
      const picker = fixture.debugElement.query(By.directive(FolderPickerComponent))
        .componentInstance as FolderPickerComponent;
      const entry = { name: 'new.mp4', length: 1, lastModifiedUtc: '2026-10-18T07:00:00Z' };
      const folder = { path: 'Show/S02', parentPath: 'Show', directories: [], files: [entry] };
      const status = () =>
        Array.from(el().querySelectorAll('[role="status"]')).map((s) => squash(s.textContent));
      picker.folderSelected.emit([folder]);
      await fixture.whenStable();
      expect(status()).toContain('Added 1 file from Show/S02.');

      fixture.componentRef.setInput('files', [...videos, ...thumbs, make('new.mp4', 'Show/S02')]);
      await fixture.whenStable();
      picker.folderSelected.emit([folder]);
      await fixture.whenStable();
      expect(status()).toContain('No new files were added; they are already in the collection.');

      picker.folderSelected.emit([{ ...folder, files: [] }]);
      await fixture.whenStable();
      expect(status()).toContain('No files were found in Show/S02.');
    });
  });
});
