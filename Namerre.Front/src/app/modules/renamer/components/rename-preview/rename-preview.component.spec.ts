import { TestBed } from '@angular/core/testing';
import type { PreviewFile, PreviewResult } from '../../rename-api';
import { RenamePreviewComponent } from './rename-preview.component';

describe('RenamePreviewComponent', () => {
  const file = (overrides: Partial<PreviewFile>): PreviewFile => ({
    id: 'a.mkv',
    sourcePath: 'a.mkv',
    targetPath: 'a.mkv',
    unchanged: true,
    steps: [],
    issues: [],
    ...overrides,
  });

  async function create() {
    await TestBed.configureTestingModule({ imports: [RenamePreviewComponent] }).compileComponents();
    return TestBed.createComponent(RenamePreviewComponent);
  }

  it('renders only confirmed filenames and emits refresh requests without API providers', async () => {
    const fixture = await create();
    const result: PreviewResult = {
      canPrepare: false,
      files: [file({ issues: [{ code: 'MetadataRequired', message: 'Match required.' }] })],
    };
    fixture.componentRef.setInput('result', result);
    const refresh = vi.fn();
    fixture.componentInstance.previewRequested.subscribe(refresh);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('TMDB matching');
    expect(fixture.nativeElement.textContent).toContain('Needs match');
    fixture.componentRef.setInput('result', {
      canPrepare: true,
      files: [file({ targetPath: 'Movie.mkv', unchanged: false })],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Movie.mkv');
    const refreshButton = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((button) => button.textContent?.trim() === 'Refresh preview');
    refreshButton?.click();
    expect(refresh).toHaveBeenCalledOnce();
    fixture.destroy();
  });

  it('summarises the result, filters rows and shows problems with the message first', async () => {
    const fixture = await create();
    fixture.componentRef.setInput('result', {
      canPrepare: false,
      files: [
        file({ id: '1', sourcePath: 'in/a.mkv', targetPath: 'out/A.mkv', unchanged: false }),
        file({ id: '2', sourcePath: 'b.mkv', targetPath: 'b.mkv' }),
        file({
          id: '3',
          sourcePath: 'c.mkv',
          targetPath: 'c.mkv',
          issues: [{ code: 'TargetExists', message: 'That name is taken.' }],
          steps: [
            {
              instanceId: 'regex-guid',
              typeId: 'regex',
              inputPath: 'c.mkv',
              outputPath: null,
              error: null,
            },
          ],
        }),
      ],
    } satisfies PreviewResult);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const text = () => root.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(text()).toContain('1 will be renamed · 1 unchanged · 1 problem');
    expect(root.querySelectorAll('tbody').length).toBe(3);
    expect(text()).toContain('moves to out');
    expect(text()).toContain('That name is taken. (TargetExists)');
    expect(text()).toContain('regex');
    expect(text()).not.toContain('regex-guid');

    const filterButton = (label: string) =>
      Array.from(root.querySelectorAll('[role="group"] button')).find((button) =>
        button.textContent?.trim().startsWith(label),
      ) as HTMLButtonElement;
    expect(filterButton('All').getAttribute('aria-pressed')).toBe('true');
    expect(filterButton('Problems').textContent).toContain('(1)');
    filterButton('Problems').click();
    await fixture.whenStable();
    expect(filterButton('Problems').getAttribute('aria-pressed')).toBe('true');
    expect(filterButton('All').getAttribute('aria-pressed')).toBe('false');
    expect(root.querySelectorAll('tbody').length).toBe(1);
    expect(text()).toContain('c.mkv');
    expect(root.querySelector('tbody td')?.textContent?.trim()).toMatch(/3$/);
    expect(text()).not.toContain('b.mkv');
    filterButton('Unchanged').click();
    await fixture.whenStable();
    expect(text()).toContain('b.mkv');
    expect(text()).not.toContain('That name is taken');
    fixture.destroy();
  });
  it('leaves out problem files one at a time or all together, but not files awaiting a match', async () => {
    const fixture = await create();
    const clash = {
      code: 'DuplicateTarget',
      message: 'Multiple files resolve to this destination.',
    };
    fixture.componentRef.setInput('result', {
      canPrepare: false,
      files: [
        file({
          id: 'a.1.en.srt',
          sourcePath: 'a.1.en.srt',
          targetPath: 'A.en.srt',
          unchanged: false,
          issues: [clash],
        }),
        file({
          id: 'a.2.en.srt',
          sourcePath: 'a.2.en.srt',
          targetPath: 'A.en.srt',
          unchanged: false,
          issues: [clash],
        }),
        file({
          id: 'b.mkv',
          sourcePath: 'b.mkv',
          issues: [{ code: 'MetadataRequired', message: 'Match.' }],
        }),
        file({ id: 'c.mkv', sourcePath: 'c.mkv', targetPath: 'C.mkv', unchanged: false }),
      ],
    } satisfies PreviewResult);
    const excluded = vi.fn();
    fixture.componentInstance.filesExcluded.subscribe(excluded);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const buttons = () => Array.from(root.querySelectorAll('button')) as HTMLButtonElement[];
    const rowButtons = buttons().filter((button) => button.textContent?.trim() === 'Leave out');
    expect(rowButtons.map((button) => button.getAttribute('aria-label'))).toEqual([
      'Leave out a.1.en.srt',
      'Leave out a.2.en.srt',
    ]);
    rowButtons[1].click();
    expect(excluded).toHaveBeenLastCalledWith(['a.2.en.srt']);
    const all = buttons().find((button) => button.textContent?.includes('with problems'));
    expect(all?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Leave out 2 files with problems');
    all?.click();
    expect(excluded).toHaveBeenLastCalledWith(['a.1.en.srt', 'a.2.en.srt']);
    fixture.destroy();
  });
});
