import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { stubDialogsThatToggle } from '../../../../dialogs.testing';
import type { RunView } from '../../rename-models';
import { RunDeleteDialogComponent } from './run-delete-dialog.component';

describe('RunDeleteDialogComponent', () => {
  let fixture: ComponentFixture<RunDeleteDialogComponent>;
  let restoreDialogs: () => void;
  const run = { id: 'run-1', kind: 'Rename', status: 'Completed', files: [] } as unknown as RunView;
  const text = () => fixture.nativeElement.textContent as string;

  beforeEach(async () => {
    restoreDialogs = stubDialogsThatToggle();
    fixture = TestBed.createComponent(RunDeleteDialogComponent);
    fixture.componentRef.setInput('run', run);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    restoreDialogs();
  });

  it('warns that a rename takes its undo records and saved settings along', async () => {
    expect(fixture.nativeElement.querySelector('h2').textContent).toBe(
      'Delete this rename record?',
    );
    expect(text()).toContain('This also deletes its undo records and its saved settings.');
    fixture.componentRef.setInput('run', { ...run, kind: 'Rollback' });
    await fixture.whenStable();
    expect(text()).not.toContain('undo records');
    expect(text()).toContain('Files on disk are not touched');
  });

  it('shows progress and a refusal from the parent', async () => {
    fixture.componentRef.setInput('deleting', true);
    fixture.componentRef.setInput('error', 'Refused.');
    await fixture.whenStable();
    expect(text()).toContain('Deleting…');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toBe('Refused.');
  });

  it('passes the answer on', () => {
    const confirmed = vi.fn();
    const cancelled = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentInstance.cancelled.subscribe(cancelled);
    const buttons = fixture.nativeElement.querySelectorAll(
      'button',
    ) as NodeListOf<HTMLButtonElement>;
    buttons[0].click();
    buttons[1].click();
    expect(cancelled).toHaveBeenCalledTimes(1);
    expect(confirmed).toHaveBeenCalledTimes(1);
  });
});
