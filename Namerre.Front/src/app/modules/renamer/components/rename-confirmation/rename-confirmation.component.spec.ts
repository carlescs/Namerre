import { TestBed } from '@angular/core/testing';
import { PlanView } from '../../rename-api';
import { RenameConfirmationComponent } from './rename-confirmation.component';

describe('RenameConfirmationComponent', () => {
  it('shows the prepared snapshot and emits confirmation only when enabled', async () => {
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
    fixture.componentRef.setInput('canConfirm', true);
    fixture.componentRef.setInput('canRetry', true);
    await fixture.whenStable();
    button.click();
    expect(confirm).toHaveBeenCalledOnce();
    fixture.nativeElement.querySelectorAll('button')[1].click();
    expect(retry).toHaveBeenCalledOnce();
    fixture.destroy();
  });
});
