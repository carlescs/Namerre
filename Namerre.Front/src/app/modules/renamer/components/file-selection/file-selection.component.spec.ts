import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { RenamerFile } from '../../renamer-file';
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
    const changed = vi.fn();
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
});
