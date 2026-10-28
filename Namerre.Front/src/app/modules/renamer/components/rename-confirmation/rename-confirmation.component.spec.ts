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
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
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
    fixture.nativeElement.querySelectorAll('button')[1].click();
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
});
