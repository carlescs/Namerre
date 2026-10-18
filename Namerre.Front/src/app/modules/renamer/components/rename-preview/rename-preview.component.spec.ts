import { TestBed } from '@angular/core/testing';
import { PreviewResult } from '../../rename-api';
import { RenamePreviewComponent } from './rename-preview.component';

describe('RenamePreviewComponent', () => {
  it('renders only confirmed filenames and emits refresh requests without API providers', async () => {
    await TestBed.configureTestingModule({ imports: [RenamePreviewComponent] }).compileComponents();
    const fixture = TestBed.createComponent(RenamePreviewComponent);
    const result: PreviewResult = {
      canPrepare: false,
      files: [
        {
          id: 'a.mkv',
          sourcePath: 'a.mkv',
          targetPath: 'a.mkv',
          unchanged: true,
          steps: [],
          issues: [{ code: 'MetadataRequired', message: 'Match required.' }],
        },
      ],
    };
    fixture.componentRef.setInput('result', result);
    const refresh = vi.fn();
    fixture.componentInstance.previewRequested.subscribe(refresh);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Final target:');
    expect(fixture.nativeElement.textContent).toContain('TMDB matching');
    fixture.componentRef.setInput('result', {
      canPrepare: true,
      files: [{ ...result.files[0], targetPath: 'Movie.mkv', unchanged: false, issues: [] }],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Final target: Movie.mkv');
    fixture.nativeElement.querySelector('button').click();
    expect(refresh).toHaveBeenCalledOnce();
    fixture.destroy();
  });
});
