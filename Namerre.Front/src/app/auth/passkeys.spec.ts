import {
  authenticationJson,
  base64UrlToBuffer,
  bufferToBase64Url,
  creationOptions,
  passkeyCancelled,
  passkeysSupported,
  requestOptions,
} from './passkeys';

describe('passkeys', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('round-trips base64url without padding', () => {
    for (const bytes of [[], [0], [251, 255], [1, 2, 3], [255, 254, 253, 252]]) {
      const encoded = bufferToBase64Url(new Uint8Array(bytes));
      expect(encoded).not.toMatch(/[+/=]/);
      expect([...new Uint8Array(base64UrlToBuffer(encoded))]).toEqual(bytes);
    }
    expect(bufferToBase64Url(new Uint8Array([251, 255]))).toBe('-_8');
  });

  it('decodes the binary fields of registration options', () => {
    const options = creationOptions({
      rp: { id: 'localhost', name: 'Namerre' },
      user: { id: 'AQI', name: 'alice', displayName: 'alice' },
      challenge: 'AAEC',
      pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
      excludeCredentials: [{ type: 'public-key', id: 'Aw' }],
    });
    expect([...new Uint8Array(options.user.id as ArrayBuffer)]).toEqual([1, 2]);
    expect([...new Uint8Array(options.challenge as ArrayBuffer)]).toEqual([0, 1, 2]);
    expect([...new Uint8Array(options.excludeCredentials?.[0]?.id as ArrayBuffer)]).toEqual([3]);
  });

  it('decodes sign-in options and encodes the answer as WebAuthn JSON', () => {
    const options = requestOptions({ challenge: 'AAEC', rpId: 'localhost', allowCredentials: [] });
    expect(options.rpId).toBe('localhost');
    expect(options.allowCredentials).toEqual([]);

    const credential = {
      id: 'Bw',
      rawId: new Uint8Array([7]).buffer,
      type: 'public-key',
      response: {
        clientDataJSON: new Uint8Array([1]).buffer,
        authenticatorData: new Uint8Array([2]).buffer,
        signature: new Uint8Array([3]).buffer,
        userHandle: new Uint8Array([4]).buffer,
      },
      getClientExtensionResults: () => ({}),
    } as unknown as PublicKeyCredential;
    expect(authenticationJson(credential)).toEqual({
      id: 'Bw',
      rawId: 'Bw',
      type: 'public-key',
      response: {
        clientDataJSON: 'AQ',
        authenticatorData: 'Ag',
        signature: 'Aw',
        userHandle: 'BA',
      },
      clientExtensionResults: {},
    });
  });

  it('needs WebAuthn in a secure context', () => {
    vi.stubGlobal('isSecureContext', true);
    vi.stubGlobal('PublicKeyCredential', undefined);
    expect(passkeysSupported()).toBe(false);
    vi.stubGlobal('PublicKeyCredential', class {});
    vi.stubGlobal('navigator', { credentials: { get: () => null, create: () => null } });
    expect(passkeysSupported()).toBe(true);
    vi.stubGlobal('isSecureContext', false);
    expect(passkeysSupported()).toBe(false);
  });

  it('treats a dismissed browser prompt as a cancellation', () => {
    expect(passkeyCancelled(new DOMException('closed', 'NotAllowedError'))).toBe(true);
    expect(passkeyCancelled(new DOMException('aborted', 'AbortError'))).toBe(true);
    expect(passkeyCancelled(new DOMException('exists', 'InvalidStateError'))).toBe(false);
    expect(passkeyCancelled(new Error('offline'))).toBe(false);
  });
});
