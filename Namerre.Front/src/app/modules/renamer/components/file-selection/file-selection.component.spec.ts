import { type CdkDragDrop, CdkDropList } from '@angular/cdk/drag-drop';
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
    /** A cell's text with its parts (such as name, folder and note) separated by spaces. */
    const cellText = (cell: Element | null) =>
      squash(
        Array.from(cell?.childNodes ?? [])
          .filter((node) => node.nodeType !== Node.COMMENT_NODE)
          .map((node) => node.textContent)
          .join(' '),
      );
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
      expect(el().querySelector('th:nth-child(3)')?.textContent?.trim()).toBe('Size');
      const cell = rows()[0].querySelector('td:nth-child(3)') as HTMLElement;
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

    describe('related files', () => {
      const related = () => el().querySelector('#include-related') as HTMLInputElement;
      const notes = () =>
        Array.from(rows()).map((row) => cellText(row.querySelector('td:nth-child(2)')));

      it('groups artwork with its video and numbers only the videos', () => {
        expect(related().checked).toBe(true);
        expect(notes()).toEqual([
          'e1.mp4 Show/S01 With 1 related file',
          'e1-thumb.jpg Show/S01 Follows e1.mp4',
          'e2.mp4 Show/S01 With 1 related file',
          'e2-thumb.jpg Show/S01 Follows e2.mp4',
          'e3.MP4 Show/S01',
        ]);
        expect(el().textContent).toContain('Related files stay with their video');
        expect(Array.from(rows()).map((row) => cellText(row.querySelector('td')))).toEqual([
          '1',
          '↳ Related to e1.mp4',
          '2',
          '↳ Related to e2.mp4',
          '3',
        ]);
      });

      it('removes a video together with its related files', () => {
        const remove = el().querySelector(
          'button[aria-label="Remove Show/S01/e1.mp4 from collection"]',
        ) as HTMLButtonElement;
        remove.click();
        expect(changed).toHaveBeenCalledWith([videos[1], videos[2], thumbs[1]]);
        fixture.detectChanges();
        expect(el().textContent).toContain('Removed e1.mp4 and its 1 related file.');
      });

      it('treats every file on its own once the option is turned off', () => {
        related().click();
        fixture.detectChanges();
        expect(fixture.componentInstance.includeRelated()).toBe(false);
        expect(notes()).toEqual([
          'e1.mp4 Show/S01',
          'e2.mp4 Show/S01',
          'e3.MP4 Show/S01',
          'e1-thumb.jpg Show/S01',
          'e2-thumb.jpg Show/S01',
        ]);
        expect(
          Array.from(rows()).map((row) => squash(row.querySelector('td')?.textContent)),
        ).toEqual(['1', '2', '3', '4', '5']);
        (
          el().querySelector(
            'button[aria-label="Remove Show/S01/e1.mp4 from collection"]',
          ) as HTMLButtonElement
        ).click();
        expect(changed).toHaveBeenCalledWith([videos[1], videos[2], ...thumbs]);
      });
    });

    it('offers no bulk removal without a filter', () => {
      expect(button('Remove')).toBeUndefined();
      expect(button('Clear collection')).toBeDefined();
    });

    describe('ordering', () => {
      const select = () => el().querySelector('select') as HTMLSelectElement;
      const names = (files: RenamerFile[]) => files.map((file) => file.name);
      const status = () =>
        Array.from(el().querySelectorAll('[role="status"]')).map((s) => squash(s.textContent));
      const moveButton = (path: string, direction: 'up' | 'down') =>
        el().querySelector(`button[aria-label="Move ${path} ${direction}"]`) as HTMLButtonElement;
      const dropList = () =>
        fixture.debugElement.query(By.directive(CdkDropList)).injector.get(CdkDropList);
      const lastEmitted = () => changed.mock.lastCall?.[0] ?? [];

      describe('with related files', () => {
        it('keeps related files right after their video when sorting and adding', async () => {
          select().value = 'name-desc';
          select().dispatchEvent(new Event('change'));
          expect(names(lastEmitted())).toEqual([
            'e3.MP4',
            'e2.mp4',
            'e2-thumb.jpg',
            'e1.mp4',
            'e1-thumb.jpg',
          ]);
          fixture.componentRef.setInput('files', lastEmitted());
          await fixture.whenStable();
          expect(select().selectedOptions[0].textContent?.trim()).toBe('Name (Z to A)');

          const picker = fixture.debugElement.query(By.directive(FolderPickerComponent))
            .componentInstance as FolderPickerComponent;
          const entry = (name: string) => ({ name, length: 1, lastModifiedUtc: '2026-10-18' });
          picker.folderSelected.emit([
            {
              path: 'Show/S01',
              parentPath: 'Show',
              directories: [],
              files: [entry('e3.en.srt'), entry('e4.mkv')],
            },
          ]);
          expect(names(lastEmitted())).toEqual([
            'e3.MP4',
            'e3.en.srt',
            'e2.mp4',
            'e2-thumb.jpg',
            'e1.mp4',
            'e1-thumb.jpg',
            'e4.mkv',
          ]);
        });

        it('moves a video with its related files and offers no moves for related files', () => {
          fixture.componentRef.setInput('files', [videos[0], thumbs[0], videos[1], thumbs[1]]);
          fixture.detectChanges();
          expect(moveButton(thumbs[0].path, 'down')).toBeNull();
          moveButton(videos[0].path, 'down').click();
          fixture.detectChanges();
          expect(names(lastEmitted())).toEqual([
            'e2.mp4',
            'e2-thumb.jpg',
            'e1.mp4',
            'e1-thumb.jpg',
          ]);
          expect(status()).toContain('Moved e1.mp4 to position 2 of 2.');
        });

        it('drags a video and its related files as one group', () => {
          fixture.componentRef.setInput('files', [videos[0], thumbs[0], videos[1], thumbs[1]]);
          fixture.detectChanges();
          const groups = Array.from(el().querySelectorAll('tbody.cdk-drag'));
          expect(
            groups.map((group) =>
              Array.from(group.querySelectorAll('tr')).map(
                (row) => cellText(row.querySelectorAll('td')[1]).split(' ')[0],
              ),
            ),
          ).toEqual([
            ['e1.mp4', 'e1-thumb.jpg'],
            ['e2.mp4', 'e2-thumb.jpg'],
          ]);
          expect(el().querySelectorAll('.drag-handle')).toHaveLength(2);
          expect(dropList().data).toEqual([
            [videos[0], thumbs[0]],
            [videos[1], thumbs[1]],
          ]);
          dropList().dropped.emit({ previousIndex: 1, currentIndex: 0 } as CdkDragDrop<
            RenamerFile[][]
          >);
          expect(names(lastEmitted())).toEqual([
            'e2.mp4',
            'e2-thumb.jpg',
            'e1.mp4',
            'e1-thumb.jpg',
          ]);
          fixture.detectChanges();
          expect(status()).toContain('Moved e2.mp4 to position 1 of 2.');
        });

        it('groups the collection again when related files are turned back on', () => {
          const related = el().querySelector('#include-related') as HTMLInputElement;
          related.click();
          related.click();
          expect(names(lastEmitted())).toEqual([
            'e1.mp4',
            'e1-thumb.jpg',
            'e2.mp4',
            'e2-thumb.jpg',
            'e3.MP4',
          ]);
        });
      });

      describe('of single files', () => {
        beforeEach(() => {
          fixture.componentInstance.includeRelated.set(false);
          fixture.detectChanges();
        });

        it('numbers files in collection order and recognises a custom order', () => {
          expect(Array.from(rows()).map((row) => cellText(row.querySelector('td')))).toEqual([
            '1',
            '2',
            '3',
            '4',
            '5',
          ]);
          expect(select().selectedOptions[0].textContent?.trim()).toBe('Custom order');
        });

        it('sorts the collection naturally once and recognises the sort', async () => {
          const numbered = [make('ep10.mkv', 'B'), make('ep2.mkv', 'A'), make('Ep1.mkv', 'C')];
          fixture.componentRef.setInput('files', numbered);
          await fixture.whenStable();
          select().value = 'name';
          select().dispatchEvent(new Event('change'));
          expect(names(lastEmitted())).toEqual(['Ep1.mkv', 'ep2.mkv', 'ep10.mkv']);
          select().value = 'folder';
          select().dispatchEvent(new Event('change'));
          expect(names(lastEmitted())).toEqual(['ep2.mkv', 'ep10.mkv', 'Ep1.mkv']);

          fixture.componentRef.setInput('files', lastEmitted());
          await fixture.whenStable();
          expect(select().selectedOptions[0].textContent?.trim()).toBe('Folder, then name');
          expect(status()).toContain('Sorted by folder, then name.');
        });

        it('sorts by size and modification date in both directions', () => {
          const small = { ...make('small.mkv', 'A', 5), lastModifiedUtc: '2026-01-02T00:00:00Z' };
          const large = { ...make('large.mkv', 'A', 50), lastModifiedUtc: '2026-01-01T00:00:00Z' };
          fixture.componentRef.setInput('files', [small, large]);
          fixture.detectChanges();
          const sorted = (value: string) => {
            select().value = value;
            select().dispatchEvent(new Event('change'));
            return names(lastEmitted());
          };
          expect(sorted('size')).toEqual(['small.mkv', 'large.mkv']);
          expect(sorted('size-desc')).toEqual(['large.mkv', 'small.mkv']);
          expect(sorted('modified')).toEqual(['large.mkv', 'small.mkv']);
          expect(sorted('modified-desc')).toEqual(['small.mkv', 'large.mkv']);
        });

        it('moves files with the arrow buttons, keeps focus and announces the move', async () => {
          const files = [...videos, ...thumbs];
          expect(moveButton(files[0].path, 'up').disabled).toBe(true);
          expect(moveButton(files[4].path, 'down').disabled).toBe(true);
          moveButton(files[0].path, 'down').click();
          expect(changed).toHaveBeenLastCalledWith([files[1], files[0], ...files.slice(2)]);
          fixture.componentRef.setInput('files', lastEmitted());
          await fixture.whenStable();
          expect(document.activeElement).toBe(moveButton(files[0].path, 'down'));
          expect(status()).toContain('Moved e1.mp4 to position 2 of 5.');

          moveButton(files[0].path, 'up').click();
          fixture.componentRef.setInput('files', lastEmitted());
          await fixture.whenStable();
          expect(changed).toHaveBeenLastCalledWith(files);
          // At the top the up button is disabled, so focus moves to the down button.
          expect(document.activeElement).toBe(moveButton(files[0].path, 'down'));
        });

        it('reorders files dropped with drag and drop', () => {
          dropList().dropped.emit({ previousIndex: 4, currentIndex: 1 } as CdkDragDrop<
            RenamerFile[][]
          >);
          expect(changed).toHaveBeenLastCalledWith([
            videos[0],
            thumbs[1],
            videos[1],
            videos[2],
            thumbs[0],
          ]);
          expect(el().querySelectorAll('.drag-handle')).toHaveLength(5);
        });

        it('offers no reordering while a filter hides part of the collection', () => {
          typeText('e1');
          expect(dropList().disabled).toBe(true);
          expect(el().querySelector('button[aria-label^="Move"]')).toBeNull();
          expect(el().querySelector('.drag-handle')).toBeNull();
          expect(el().textContent).toContain('Clear the filter to change the order.');
          dropList().dropped.emit({ previousIndex: 0, currentIndex: 1 } as CdkDragDrop<
            RenamerFile[][]
          >);
          expect(changed).not.toHaveBeenCalled();
        });
      });
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
