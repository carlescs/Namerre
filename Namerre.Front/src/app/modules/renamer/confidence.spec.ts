import { HIGH_CONFIDENCE } from './rename-api';
import vectors from './test-vectors/confidence.json';

describe('High match confidence shared test vectors', () => {
  // The same vectors run against the server's MatchConfidence.High (MirroredLogicTests).
  it('uses the server threshold', () => {
    expect(HIGH_CONFIDENCE).toBe(vectors.high);
  });

  it.each(vectors.cases)('score $score is high: $isHigh', ({ score, isHigh }) => {
    expect(score >= HIGH_CONFIDENCE).toBe(isHigh);
  });
});
