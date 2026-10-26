import {
  loadFilesDraft,
  loadPipelineDraft,
  saveFilesDraft,
  savePipelineDraft,
} from './draft-storage';
import { RenamerFile } from './renamer-file';

describe('draft storage', () => {
  const file: RenamerFile = {
    name: 'a.mkv',
    path: 'Movies/a.mkv',
    folderPath: 'Movies',
    length: 1,
    lastModifiedUtc: '2026-10-18T00:00:00Z',
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('round-trips files and removes the entry when the collection empties', () => {
    saveFilesDraft([file]);
    expect(loadFilesDraft()).toEqual([file]);
    saveFilesDraft([]);
    expect(localStorage.length).toBe(0);
    expect(loadFilesDraft()).toEqual([]);
  });

  it('round-trips the pipeline and drops malformed plugins', () => {
    const plugin = { instanceId: 'i', typeId: 'case', version: 1, enabled: true, options: {} };
    savePipelineDraft({ plugins: [plugin], verifyContent: true, deleteEmptyFolders: false });
    expect(loadPipelineDraft()).toEqual({
      plugins: [plugin],
      verifyContent: true,
      deleteEmptyFolders: false,
    });
    localStorage.setItem(
      'namerre.draft.pipeline.v1',
      JSON.stringify({ plugins: [plugin, { typeId: 'case' }] }),
    );
    expect(loadPipelineDraft()?.plugins).toEqual([plugin]);
  });

  it('ignores corrupt or unavailable storage', () => {
    localStorage.setItem('namerre.draft.files.v1', '{not json');
    expect(loadFilesDraft()).toEqual([]);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(loadPipelineDraft()).toBeNull();
    expect(() => saveFilesDraft([file])).not.toThrow();
  });
});
