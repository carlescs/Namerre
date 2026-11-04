import {
  awaitingMetadata,
  isMatchingIssue,
  isSettledRun,
  type PreviewFile,
  type RunView,
} from './rename-models';

describe('rename model helpers', () => {
  it('recognises metadata issues that need matching', () => {
    expect(isMatchingIssue('MetadataRequired')).toBe(true);
    expect(isMatchingIssue('MetadataStale')).toBe(true);
    expect(isMatchingIssue('TargetExists')).toBe(false);
  });

  it('detects previews awaiting metadata', () => {
    const file = (codes: string[]): PreviewFile => ({
      id: 'f',
      sourcePath: 'a',
      targetPath: 'a',
      unchanged: true,
      steps: [],
      issues: codes.map((code) => ({ code, message: code })),
    });
    expect(awaitingMetadata(file([]))).toBe(false);
    expect(awaitingMetadata(file(['TargetExists']))).toBe(false);
    expect(awaitingMetadata(file(['TargetExists', 'MetadataStale']))).toBe(true);
  });

  it.each([
    ['Completed', true],
    ['Recovered', true],
    ['Queued', false],
    ['Running', false],
    ['RecoveryRequired', false],
    ['Recovering', false],
  ])('treats %s runs as settled: %s', (status, settled) => {
    expect(isSettledRun({ status } as RunView)).toBe(settled);
  });
});
