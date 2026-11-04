import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { Auth } from '../../../../auth/auth';
import type { RunView } from '../../rename-models';
import { RunDeleteButtonComponent } from './run-delete-button.component';

describe('RunDeleteButtonComponent', () => {
  let fixture: ComponentFixture<RunDeleteButtonComponent>;
  const run = {
    id: 'run-1',
    kind: 'Rename',
    status: 'Completed',
    files: [{ sourcePath: 'Movies/a.mkv', targetPath: 'Movies/A.mkv' }],
  } as RunView;
  const button = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement | null;

  beforeEach(async () => {
    TestBed.inject(Auth).role.set('Admin');
    fixture = TestBed.createComponent(RunDeleteButtonComponent);
    fixture.componentRef.setInput('run', run);
    await fixture.whenStable();
  });

  it('asks to delete a settled run by its id', () => {
    const requested = vi.fn();
    fixture.componentInstance.requested.subscribe(requested);
    expect(button()?.getAttribute('aria-label')).toMatch(/^Delete rename record: /);
    button()?.click();
    expect(requested).toHaveBeenCalledWith('run-1');
  });

  it('is disabled for unsettled runs and while the parent is busy', async () => {
    fixture.componentRef.setInput('run', { ...run, status: 'RecoveryRequired' });
    await fixture.whenStable();
    expect(button()?.disabled).toBe(true);
    expect(button()?.title).toBe('Only completed or recovered runs can be deleted');
    fixture.componentRef.setInput('run', run);
    fixture.componentRef.setInput('disabled', true);
    await fixture.whenStable();
    expect(button()?.disabled).toBe(true);
  });

  it('is hidden from users', async () => {
    TestBed.inject(Auth).role.set('User');
    await fixture.whenStable();
    expect(button()).toBeNull();
  });
});
