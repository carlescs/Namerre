import { TestBed } from '@angular/core/testing';
import type { PlanView } from '../../rename-api';
import { RenameConfirmationComponent } from './rename-confirmation.component';

describe('RenameConfirmationComponent', () => {
  it('shows the prepared plan and emits confirmation only when enabled', async () => {
    await TestBed.configureTestingModule({
      imports: [RenameConfirmationComponent],
    }).compileComponents();
    const fixture = TestBed.createComponent(RenameConfirmationComponent);
    const plan: PlanView = {
      id: 'plan',
      version: 1,
      status: 'Ready',
      completedFiles: 1,
      createdAt: '',
      pipeline: { files: [], plugins: [], selections: [] },
      issues: [],
      files: [{ id: 'a', sourcePath: 'a.mkv', targetPath: 'Movie.mkv', hash: 'hash', size: 1 }],
    };
    fixture.componentRef.setInput('plan', plan);
    const confirm = vi.fn();
    const retry = vi.fn();
    fixture.componentInstance.confirmRequested.subscribe(confirm);
    fixture.componentInstance.retryRequested.subscribe(retry);
    await fixture.whenStable();
    const button = fixture.nativeElement.querySelector('.btn-danger') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    button.click();
    expect(confirm).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Movie.mkv');
    expect(fixture.nativeElement.textContent).toContain('Ready to rename 1 file');
    expect(button.textContent).toContain('Rename 1 file');
    expect(fixture.nativeElement.querySelector('details summary').textContent).toContain(
      'Technical details',
    );
    expect(fixture.nativeElement.querySelector('details').open).toBe(false);
    expect(fixture.nativeElement.textContent).not.toContain('Folders left empty');
    fixture.componentRef.setInput('plan', {
      ...plan,
      pipeline: { ...plan.pipeline, deleteEmptyFolders: true },
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Folders left empty');
    fixture.componentRef.setInput('canConfirm', true);
    fixture.componentRef.setInput('canRetry', true);
    await fixture.whenStable();
    button.click();
    expect(confirm).toHaveBeenCalledOnce();
    (
      Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLElement>).find(
        (candidate) => candidate.textContent?.includes('Check again'),
      ) as HTMLButtonElement
    ).click();
    expect(retry).toHaveBeenCalledOnce();
    fixture.destroy();
  });

  it('shows only a styled progress bar while the plan is being prepared', async () => {
    await TestBed.configureTestingModule({
      imports: [RenameConfirmationComponent],
    }).compileComponents();
    const fixture = TestBed.createComponent(RenameConfirmationComponent);
    fixture.componentRef.setInput('preparing', true);
    fixture.componentRef.setInput('fileCount', 44);
    fixture.componentRef.setInput('plan', {
      id: 'plan',
      version: 1,
      status: 'Preparing',
      completedFiles: 12,
      createdAt: '',
      pipeline: { files: [], plugins: [], selections: [] },
      issues: [],
      files: [{ id: 'a', sourcePath: 'a.mkv', targetPath: 'Movie.mkv', hash: 'h', size: 1 }],
    } satisfies PlanView);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Checking 12 of 44 files…');
    const bar = root.querySelector('[role="progressbar"]');
    expect(bar?.getAttribute('aria-valuenow')).toBe('12');
    expect(bar?.getAttribute('aria-valuemax')).toBe('44');
    expect(root.querySelector('progress')).toBeNull();
    expect(root.querySelector('table')).toBeNull();
    expect(root.querySelector('.btn-danger')).toBeNull();
    fixture.destroy();
  });

  it('edits a new name in place and emits it only when accepted', async () => {
    await TestBed.configureTestingModule({
      imports: [RenameConfirmationComponent],
    }).compileComponents();
    const fixture = TestBed.createComponent(RenameConfirmationComponent);
    fixture.componentRef.setInput('plan', {
      id: 'plan',
      version: 1,
      status: 'Ready',
      completedFiles: 2,
      createdAt: '',
      pipeline: { files: [], plugins: [], selections: [] },
      issues: [],
      files: [
        { id: 'a', sourcePath: 'in/a.mkv', targetPath: 'in/Movie.mkv', hash: 'h', size: 1 },
        { id: 'b', sourcePath: 'in/b.mkv', targetPath: 'in/b.mkv', hash: 'h', size: 1 },
      ],
    } satisfies PlanView);
    const edited = vi.fn();
    fixture.componentInstance.targetEdited.subscribe(edited);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const pencil = (path: string) =>
      root.querySelector(`[aria-label="Edit the new name of ${path}"]`) as HTMLButtonElement;
    expect(pencil('in/a.mkv').disabled).toBe(true);
    expect(pencil('in/b.mkv')).not.toBeNull();
    expect(root.textContent).not.toContain('Edited by hand');
    fixture.componentRef.setInput('canEdit', true);
    await fixture.whenStable();

    pencil('in/a.mkv').click();
    await fixture.whenStable();
    let editor = root.querySelector('[aria-label="New path for in/a.mkv"]') as HTMLInputElement;
    expect(editor.value).toBe('in/Movie.mkv');
    editor.value = 'elsewhere';
    editor.dispatchEvent(new Event('input'));
    (root.querySelector('[aria-label="Cancel editing"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(root.querySelector('[aria-label="New path for in/a.mkv"]')).toBeNull();
    expect(edited).not.toHaveBeenCalled();

    pencil('in/a.mkv').click();
    await fixture.whenStable();
    editor = root.querySelector('[aria-label="New path for in/a.mkv"]') as HTMLInputElement;
    expect(editor.value).toBe('in/Movie.mkv');
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await fixture.whenStable();
    expect(root.querySelector('[aria-label="New path for in/a.mkv"]')).toBeNull();

    pencil('in/a.mkv').click();
    await fixture.whenStable();
    editor = root.querySelector('[aria-label="New path for in/a.mkv"]') as HTMLInputElement;
    editor.value = '  out/Other Movie.mkv ';
    editor.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    (root.querySelector('[aria-label="Use this name"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(edited).toHaveBeenCalledExactlyOnceWith({
      fileId: 'a',
      targetPath: 'out/Other Movie.mkv',
    });
    expect(root.querySelector('[aria-label="New path for in/a.mkv"]')).toBeNull();
    fixture.destroy();
  });

  it('marks hand-edited names and offers to restore the calculated one', async () => {
    await TestBed.configureTestingModule({
      imports: [RenameConfirmationComponent],
    }).compileComponents();
    const fixture = TestBed.createComponent(RenameConfirmationComponent);
    fixture.componentRef.setInput('canEdit', true);
    fixture.componentRef.setInput('plan', {
      id: 'plan',
      version: 1,
      status: 'Ready',
      completedFiles: 1,
      createdAt: '',
      pipeline: {
        files: [],
        plugins: [],
        selections: [],
        overrides: [{ fileId: 'a', targetPath: 'Mine.mkv' }],
      },
      issues: [],
      files: [{ id: 'a', sourcePath: 'a.mkv', targetPath: 'Mine.mkv', hash: 'h', size: 1 }],
    } satisfies PlanView);
    const reset = vi.fn();
    fixture.componentInstance.editReset.subscribe(reset);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Edited by hand');
    (
      root.querySelector('[aria-label="Restore the calculated name of a.mkv"]') as HTMLButtonElement
    ).click();
    expect(reset).toHaveBeenCalledExactlyOnceWith('a');
    fixture.destroy();
  });
});
