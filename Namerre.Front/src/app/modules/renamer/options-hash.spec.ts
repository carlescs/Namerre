import { optionsHash, tmdbOptionsHash } from './options-hash';
import { operationId } from './rename-api';

describe('Metadata option binding', () => {
  it('uses standard lowercase SHA256 including padding across multiple blocks', () => {
    expect(optionsHash('')).toBe(
      'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
    );
    expect(optionsHash('abc')).toBe(
      'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',
    );
    expect(optionsHash('a'.repeat(100))).toBe(
      '2816597888e4a0d3a36b82b83316ab32680eb8f00f8cd3b904d681246d285a0e',
    );
    expect(optionsHash('{"mode":"upper"}')).not.toBe(optionsHash('{"mode":"lower"}'));
  });

  it('binds TMDB metadata language independently of output templates', () => {
    const original = tmdbOptionsHash({ language: 'en-US', nameTemplate: '{title}' });
    expect(original).toBe(optionsHash('en-US'));
    expect(tmdbOptionsHash({})).toBe(original);
    expect(
      tmdbOptionsHash({
        nameTemplate: '{title} ({year})',
        changeFolder: true,
        folderTemplate: '{year}',
        language: 'en-US',
      }),
    ).toBe(original);
    expect(tmdbOptionsHash({ language: 'fr' })).not.toBe(original);
  });

  it('generates a UUID on insecure self-hosted origins without randomUUID', () => {
    const descriptor = Object.getOwnPropertyDescriptor(crypto, 'randomUUID');
    Object.defineProperty(crypto, 'randomUUID', { configurable: true, value: undefined });
    try {
      expect(operationId()).toMatch(
        /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/,
      );
    } finally {
      if (descriptor) Object.defineProperty(crypto, 'randomUUID', descriptor);
      else Reflect.deleteProperty(crypto, 'randomUUID');
    }
  });
});
