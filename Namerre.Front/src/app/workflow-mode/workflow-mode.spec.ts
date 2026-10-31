import { TestBed } from '@angular/core/testing';
import { savePipelineDraft } from '../modules/renamer/draft-storage';
import { buildPipeline, presetDefaults, simplePresets } from '../modules/renamer/simple-presets';
import { WorkflowMode, workflowModeStorageKey } from './workflow-mode';

const pipelineKey = 'namerre.draft.pipeline.v1';
const advancedStep = {
  instanceId: 'regex-1',
  typeId: 'regex',
  version: 1,
  enabled: true,
  options: { pattern: 'a', replacement: 'b', ignoreCase: false },
};

describe('WorkflowMode', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('uses Simple and asks for a mode until one is stored', () => {
    const mode = TestBed.inject(WorkflowMode);
    expect(mode.stored()).toBeNull();
    expect(mode.mode()).toBe('simple');
    expect(mode.onboarding()).toBe(true);
  });

  it('persists the chosen mode per browser', async () => {
    await TestBed.inject(WorkflowMode).request('advanced');
    expect(localStorage.getItem(workflowModeStorageKey)).toBe('advanced');
    TestBed.resetTestingModule();
    const mode = TestBed.inject(WorkflowMode);
    expect(mode.mode()).toBe('advanced');
    expect(mode.onboarding()).toBe(false);
  });

  it('ignores unknown stored values', () => {
    localStorage.setItem(workflowModeStorageKey, 'expert');
    expect(TestBed.inject(WorkflowMode).stored()).toBeNull();
  });

  it('works when storage is unavailable', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const mode = TestBed.inject(WorkflowMode);
    expect(mode.mode()).toBe('simple');
    await mode.request('advanced');
    expect(mode.mode()).toBe('advanced');
  });

  it('dismisses the prompt for this visit without storing a mode', () => {
    const mode = TestBed.inject(WorkflowMode);
    mode.dismiss();
    expect(mode.onboarding()).toBe(false);
    expect(mode.mode()).toBe('simple');
    expect(localStorage.getItem(workflowModeStorageKey)).toBeNull();
  });

  it('switches to Simple at once when a preset builds the draft', async () => {
    localStorage.setItem(workflowModeStorageKey, 'advanced');
    const counter = simplePresets[3];
    savePipelineDraft({
      plugins: buildPipeline(counter, presetDefaults(counter)),
      verifyContent: false,
      deleteEmptyFolders: true,
    });
    const mode = TestBed.inject(WorkflowMode);
    await mode.request('simple');
    expect(mode.mode()).toBe('simple');
    expect(mode.pending()).toBeNull();
  });

  it('asks before discarding advanced steps and keeps them when cancelled', async () => {
    localStorage.setItem(workflowModeStorageKey, 'advanced');
    savePipelineDraft({ plugins: [advancedStep], verifyContent: true, deleteEmptyFolders: false });
    const mode = TestBed.inject(WorkflowMode);
    await mode.request('simple');
    expect(mode.pending()).toBe('simple');
    expect(mode.mode()).toBe('advanced');
    mode.cancel();
    expect(mode.pending()).toBeNull();
    expect(mode.mode()).toBe('advanced');
    expect(JSON.parse(localStorage.getItem(pipelineKey) ?? 'null').plugins).toEqual([advancedStep]);
  });

  it('discards the advanced steps once confirmed', async () => {
    localStorage.setItem(workflowModeStorageKey, 'advanced');
    savePipelineDraft({ plugins: [advancedStep], verifyContent: true, deleteEmptyFolders: false });
    const mode = TestBed.inject(WorkflowMode);
    await mode.request('simple');
    mode.confirm();
    expect(mode.mode()).toBe('simple');
    expect(mode.discarded()).toBe(1);
    expect(JSON.parse(localStorage.getItem(pipelineKey) ?? 'null')).toEqual({
      plugins: [],
      verifyContent: false,
      deleteEmptyFolders: true,
      numberCollisions: false,
    });
  });

  it('hides the onboarding prompt while the warning is open', async () => {
    savePipelineDraft({ plugins: [advancedStep], verifyContent: false, deleteEmptyFolders: true });
    const mode = TestBed.inject(WorkflowMode);
    await mode.request('simple');
    expect(mode.onboarding()).toBe(false);
    mode.cancel();
    expect(mode.onboarding()).toBe(true);
  });
});
