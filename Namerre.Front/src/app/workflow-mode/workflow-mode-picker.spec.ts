import { TestBed } from '@angular/core/testing';
import { WorkflowMode, workflowModeStorageKey } from './workflow-mode';
import { WorkflowModePicker } from './workflow-mode-picker';

describe('WorkflowModePicker', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('marks the active mode and switches to another one', async () => {
    const fixture = TestBed.createComponent(WorkflowModePicker);
    await fixture.whenStable();
    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    expect(buttons.map((button) => button.textContent?.trim())).toEqual(['Simple', 'Advanced']);
    expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual(['true', 'false']);

    buttons[1].click();
    await vi.waitFor(() => expect(TestBed.inject(WorkflowMode).mode()).toBe('advanced'));
    await fixture.whenStable();
    expect(buttons[1].getAttribute('aria-pressed')).toBe('true');
    expect(localStorage.getItem(workflowModeStorageKey)).toBe('advanced');

    buttons[0].click();
    await vi.waitFor(() => expect(TestBed.inject(WorkflowMode).mode()).toBe('simple'));
    expect(localStorage.getItem(workflowModeStorageKey)).toBe('simple');
  });
});
