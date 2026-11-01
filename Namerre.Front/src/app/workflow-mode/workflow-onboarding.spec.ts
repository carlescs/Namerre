import { TestBed } from '@angular/core/testing';
import { stubDialogs } from '../dialogs.testing';
import { savePipelineDraft } from '../modules/renamer/draft-storage';
import { WorkflowMode, workflowModeStorageKey } from './workflow-mode';
import { WorkflowModeWarning } from './workflow-mode-warning';
import { WorkflowOnboarding } from './workflow-onboarding';

function button(element: HTMLElement, label: string): HTMLButtonElement {
  const found = [...element.querySelectorAll('button')].find((item) =>
    item.textContent?.includes(label),
  );
  if (!found) throw new Error(`No button ${label}`);
  return found;
}

describe('WorkflowOnboarding', () => {
  let restoreDialogs: () => void;
  beforeEach(() => {
    localStorage.clear();
    restoreDialogs = stubDialogs(vi.fn(), vi.fn());
  });
  afterEach(() => {
    restoreDialogs();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('explains both modes in a modal and saves the choice', async () => {
    const fixture = TestBed.createComponent(WorkflowOnboarding);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(HTMLDialogElement.prototype.showModal).toHaveBeenCalled();
    expect(element.querySelector('h2')?.textContent).toBe('How would you like to rename files?');
    expect(button(element, 'Simple').textContent).toContain('preset');
    button(element, 'Advanced').click();
    await vi.waitFor(() => expect(localStorage.getItem(workflowModeStorageKey)).toBe('advanced'));
    const mode = TestBed.inject(WorkflowMode);
    expect(mode.mode()).toBe('advanced');
    expect(mode.onboarding()).toBe(false);
  });

  it('uses Simple for this visit without storing it when dismissed', async () => {
    const fixture = TestBed.createComponent(WorkflowOnboarding);
    await fixture.whenStable();
    button(fixture.nativeElement, 'Decide later').click();
    const mode = TestBed.inject(WorkflowMode);
    expect(mode.onboarding()).toBe(false);
    expect(mode.mode()).toBe('simple');
    expect(localStorage.getItem(workflowModeStorageKey)).toBeNull();
  });

  it('treats Escape as decide later', async () => {
    const fixture = TestBed.createComponent(WorkflowOnboarding);
    await fixture.whenStable();
    fixture.nativeElement
      .querySelector('dialog')
      .dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(TestBed.inject(WorkflowMode).onboarding()).toBe(false);
  });
});

describe('WorkflowModeWarning', () => {
  const step = {
    instanceId: 'regex-1',
    typeId: 'regex',
    version: 1,
    enabled: true,
    options: { pattern: 'a', replacement: 'b', ignoreCase: false },
  };

  let restoreDialogs: () => void;

  beforeEach(async () => {
    localStorage.clear();
    localStorage.setItem(workflowModeStorageKey, 'advanced');
    savePipelineDraft({ plugins: [step], verifyContent: false, deleteEmptyFolders: true });
    restoreDialogs = stubDialogs(vi.fn(), vi.fn());
    await TestBed.inject(WorkflowMode).request('simple');
  });
  afterEach(() => {
    restoreDialogs();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('keeps Advanced and the steps when cancelled', async () => {
    const fixture = TestBed.createComponent(WorkflowModeWarning);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain(
      'This pipeline uses advanced steps. Switching to Simple will discard them.',
    );
    button(fixture.nativeElement, 'Keep Advanced').click();
    const mode = TestBed.inject(WorkflowMode);
    expect(mode.pending()).toBeNull();
    expect(mode.mode()).toBe('advanced');
    expect(localStorage.getItem('namerre.draft.pipeline.v1')).toContain('regex-1');
  });

  it('starts a fresh draft in Simple when confirmed', async () => {
    const fixture = TestBed.createComponent(WorkflowModeWarning);
    await fixture.whenStable();
    button(fixture.nativeElement, 'Discard steps and switch').click();
    const mode = TestBed.inject(WorkflowMode);
    expect(mode.mode()).toBe('simple');
    expect(mode.discarded()).toBe(1);
    expect(localStorage.getItem('namerre.draft.pipeline.v1')).not.toContain('regex-1');
  });
});
