import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FolderListing } from '../../folder-listing';
import { RenamerComponent } from './renamer.component';

describe('RenamerComponent', () => {
  let component: RenamerComponent;
  let fixture: ComponentFixture<RenamerComponent>;
  let http: HttpTestingController;
  const root: FolderListing = {
    path: '',
    parentPath: null,
    directories: [{ name: 'Movies', path: 'Movies' }],
    files: [{ name: 'notes.txt', length: 12, lastModifiedUtc: '2026-10-18T07:00:00Z' }],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RenamerComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(RenamerComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/folders?path=').flush(root);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('lists the configured server root and its immediate files', () => {
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Selected folder: Server root');
    expect(element.textContent).toContain('notes.txt');
    expect(element.textContent).toContain('Movies');
    expect(element.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('navigates into a server folder and back to its parent', async () => {
    clickButton('Movies');
    const request = http.expectOne('/api/folders?path=Movies');
    expect(fixture.nativeElement.textContent).toContain('Loading folder...');
    request.flush({
      path: 'Movies',
      parentPath: '',
      directories: [],
      files: [],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Selected folder: Movies');
    expect(fixture.nativeElement.textContent).toContain('No files in this folder.');
    clickButton('Parent folder');
    http.expectOne('/api/folders?path=').flush(root);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('notes.txt');
  });

  it('shows failures without replacing the selected folder and supports retry', async () => {
    clickButton('Movies');
    http.expectOne('/api/folders?path=Movies').flush({}, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'The server cannot access this folder.',
    );
    expect(fixture.nativeElement.textContent).toContain('Selected folder: Server root');
    clickButton('Try again');
    http.expectOne('/api/folders?path=Movies').flush({
      path: 'Movies',
      parentPath: '',
      directories: [],
      files: [],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  function clickButton(label: string): void {
    const element: HTMLElement = fixture.nativeElement;
    const button = Array.from(element.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    expect(button).toBeDefined();
    button!.click();
    fixture.detectChanges();
  }
});
